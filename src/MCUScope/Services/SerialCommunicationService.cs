using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MCUScope.Services
{
    public enum ConnectionStatus
    {
        Disconnected,
        IcsUnitOnly,
        Connected
    }

    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        public ConnectionStatus Status { get; set; }
        public string StatusText { get; set; } = string.Empty;
    }

    public class DataReceivedEventArgs : EventArgs
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    public class SerialCommunicationService : IDisposable
    {
        private SerialPort? _serialPort;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private readonly object _lock = new();

        public event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
        public event EventHandler<DataReceivedEventArgs>? DataReceived;

        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
        public string PortName => _serialPort?.PortName ?? string.Empty;
        public bool IsOpen => _serialPort?.IsOpen ?? false;

        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        public bool Open(string portName, int baudRate)
        {
            try
            {
                Close();

                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                    ReadBufferSize = 65536,
                    WriteBufferSize = 65536
                };

                _serialPort.Open();
                _cts = new CancellationTokenSource();
                _readTask = Task.Run(() => ReadLoop(_cts.Token));

                UpdateStatus(ConnectionStatus.IcsUnitOnly,
                    $"Disconnected, Scope: MCUScope, Port: {portName}");
                return true;
            }
            catch (Exception)
            {
                UpdateStatus(ConnectionStatus.Disconnected, "Disconnected");
                return false;
            }
        }

        public void Close()
        {
            _cts?.Cancel();
            _readTask?.Wait(1000);
            _readTask = null;
            _cts?.Dispose();
            _cts = null;

            if (_serialPort?.IsOpen == true)
            {
                _serialPort.Close();
            }
            _serialPort?.Dispose();
            _serialPort = null;

            UpdateStatus(ConnectionStatus.Disconnected, "Disconnected");
        }

        public bool Send(byte[] data)
        {
            lock (_lock)
            {
                if (_serialPort?.IsOpen != true) return false;
                try
                {
                    _serialPort.Write(data, 0, data.Length);
                    return true;
                }
                catch (Exception ex)
                {
                    LogService.Warn($"Serial Send failed: {ex.Message}");
                    return false;
                }
            }
        }

        public bool SendCommand(byte command, byte[] payload)
        {
            var packet = BuildPacket(command, payload);
            return Send(packet);
        }

        private void ReadLoop(CancellationToken token)
        {
            var buffer = new byte[4096];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_serialPort?.IsOpen != true)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    int bytesRead = _serialPort.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        var data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);
                        DataReceived?.Invoke(this, new DataReceivedEventArgs { Data = data });
                    }
                }
                catch (TimeoutException)
                {
                    // Normal timeout, continue
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        LogService.Warn($"Serial ReadLoop error: {ex.Message}");
                        Thread.Sleep(100);
                    }
                }
            }
        }

        // ICS++ protocol packet builder
        // Frame format: [SYNC(2)] [CMD(1)] [LEN(2)] [PAYLOAD(n)] [CHECKSUM(1)]
        private static readonly byte[] SyncBytes = { 0xAA, 0x55 };

        public static byte[] BuildPacket(byte command, byte[] payload)
        {
            int len = payload.Length;
            var packet = new byte[6 + len];
            packet[0] = SyncBytes[0];
            packet[1] = SyncBytes[1];
            packet[2] = command;
            packet[3] = (byte)(len & 0xFF);
            packet[4] = (byte)((len >> 8) & 0xFF);
            Array.Copy(payload, 0, packet, 5, len);

            byte checksum = 0;
            for (int i = 2; i < 5 + len; i++)
                checksum += packet[i];
            packet[5 + len] = checksum;

            return packet;
        }

        public static (byte command, byte[] payload)? ParsePacket(byte[] data, int offset = 0)
        {
            if (data.Length - offset < 6) return null;
            if (data[offset] != SyncBytes[0] || data[offset + 1] != SyncBytes[1]) return null;

            byte command = data[offset + 2];
            int len = data[offset + 3] | (data[offset + 4] << 8);
            if (data.Length - offset < 6 + len) return null;

            var payload = new byte[len];
            Array.Copy(data, offset + 5, payload, 0, len);

            byte checksum = 0;
            for (int i = offset + 2; i < offset + 5 + len; i++)
                checksum += data[i];
            if (data[offset + 5 + len] != checksum) return null;

            return (command, payload);
        }

        private void UpdateStatus(ConnectionStatus status, string text)
        {
            Status = status;
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs
            {
                Status = status,
                StatusText = text
            });
        }

        public void SetConnected(string cpuName, string libraryVersion, double clockMHz)
        {
            UpdateStatus(ConnectionStatus.Connected,
                $"Connected, CPU:{cpuName}, Library: {libraryVersion}, Scope: MCUScope, Clock: {clockMHz}MHz");
        }

        public void Dispose()
        {
            Close();
        }
    }
}
