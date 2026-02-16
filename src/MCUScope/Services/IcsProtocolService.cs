using MCUScope.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MCUScope.Services
{
    // ICS++ Protocol Commands
    public static class IcsCommands
    {
        public const byte Ping = 0x01;
        public const byte GetInfo = 0x02;
        public const byte ReadVariable = 0x10;
        public const byte WriteVariable = 0x11;
        public const byte StartScope = 0x20;
        public const byte StopScope = 0x21;
        public const byte ScopeData = 0x22;
        public const byte SetChannels = 0x23;
        public const byte SetTrigger = 0x24;
        public const byte SetSampling = 0x25;
        public const byte Ack = 0x80;
        public const byte Nack = 0x81;
        public const byte InfoResponse = 0x82;
        public const byte VariableData = 0x83;
        public const byte WaveformData = 0x84;
    }

    public class WaveformDataEventArgs : EventArgs
    {
        public int ChannelIndex { get; set; }
        public double[] Data { get; set; } = Array.Empty<double>();
        public double SamplePeriod { get; set; }
    }

    public class VariableReadEventArgs : EventArgs
    {
        public string VariableName { get; set; } = string.Empty;
        public double Value { get; set; }
    }

    public class ScopeChannelRequest
    {
        public int ChannelIndex { get; set; }
        public uint Address { get; set; }
        public VariableType Type { get; set; }
    }

    public class IcsProtocolService : IDisposable
    {
        private readonly SerialCommunicationService _serial;
        private readonly List<byte> _receiveBuffer = new();
        private readonly object _bufferLock = new();

        public event EventHandler<WaveformDataEventArgs>? WaveformDataReceived;
        public event EventHandler<VariableReadEventArgs>? VariableValueReceived;

        public List<VariableInfo> Variables { get; private set; } = new();

        public IcsProtocolService(SerialCommunicationService serial)
        {
            _serial = serial;
            _serial.DataReceived += OnDataReceived;
        }

        private void OnDataReceived(object? sender, DataReceivedEventArgs e)
        {
            lock (_bufferLock)
            {
                _receiveBuffer.AddRange(e.Data);
                ProcessBuffer();
            }
        }

        private void ProcessBuffer()
        {
            while (_receiveBuffer.Count >= 6)
            {
                // Find sync bytes
                int syncIndex = -1;
                for (int i = 0; i < _receiveBuffer.Count - 1; i++)
                {
                    if (_receiveBuffer[i] == 0xAA && _receiveBuffer[i + 1] == 0x55)
                    {
                        syncIndex = i;
                        break;
                    }
                }

                if (syncIndex < 0)
                {
                    _receiveBuffer.Clear();
                    return;
                }

                if (syncIndex > 0)
                    _receiveBuffer.RemoveRange(0, syncIndex);

                if (_receiveBuffer.Count < 6) return;

                int len = _receiveBuffer[3] | (_receiveBuffer[4] << 8);
                int totalLen = 6 + len;

                if (_receiveBuffer.Count < totalLen) return;

                var packetData = _receiveBuffer.Take(totalLen).ToArray();
                _receiveBuffer.RemoveRange(0, totalLen);

                var parsed = SerialCommunicationService.ParsePacket(packetData);
                if (parsed.HasValue)
                {
                    HandlePacket(parsed.Value.command, parsed.Value.payload);
                }
            }
        }

        private void HandlePacket(byte command, byte[] payload)
        {
            switch (command)
            {
                case IcsCommands.InfoResponse:
                    HandleInfoResponse(payload);
                    break;
                case IcsCommands.VariableData:
                    HandleVariableData(payload);
                    break;
                case IcsCommands.WaveformData:
                    HandleWaveformData(payload);
                    break;
            }
        }

        private void HandleInfoResponse(byte[] payload)
        {
            if (payload.Length < 4) return;
            // Parse CPU info from response
            // Format: [cpuNameLen(1)][cpuName(n)][libVersionLen(1)][libVersion(n)][clock(4)]
            try
            {
                int offset = 0;
                int nameLen = payload[offset++];
                string cpuName = System.Text.Encoding.ASCII.GetString(payload, offset, nameLen);
                offset += nameLen;
                int verLen = payload[offset++];
                string libVersion = System.Text.Encoding.ASCII.GetString(payload, offset, verLen);
                offset += verLen;
                float clock = BitConverter.ToSingle(payload, offset);
                _serial.SetConnected(cpuName, libVersion, clock);
            }
            catch { }
        }

        private void HandleVariableData(byte[] payload)
        {
            if (payload.Length < 3) return;
            // Format: [nameLen(1)][name(n)][type(1)][value(4)]
            try
            {
                int offset = 0;
                int nameLen = payload[offset++];
                if (payload.Length < 2 + nameLen) return;
                string name = System.Text.Encoding.ASCII.GetString(payload, offset, nameLen);
                offset += nameLen;
                byte type = payload[offset++];
                int valueSize = GetTypeByteSize((VariableType)type);
                if (payload.Length < offset + valueSize) return;
                double value = DecodeValue(payload, offset, (VariableType)type);

                var variable = FindVariableByAnyName(name);
                if (variable != null)
                    value *= variable.Scale;

                VariableValueReceived?.Invoke(this, new VariableReadEventArgs
                {
                    VariableName = name,
                    Value = value
                });
            }
            catch { }
        }

        private void HandleWaveformData(byte[] payload)
        {
            if (payload.Length < 6) return;
            // Format: [channelIndex(1)][samplePeriod(4)][dataCount(2)][data(n*4)]
            try
            {
                int offset = 0;
                int channelIndex = payload[offset++];
                float samplePeriod = BitConverter.ToSingle(payload, offset);
                offset += 4;
                int count = payload[offset] | (payload[offset + 1] << 8);
                offset += 2;

                var data = new double[count];
                for (int i = 0; i < count && offset + 4 <= payload.Length; i++)
                {
                    data[i] = BitConverter.ToSingle(payload, offset);
                    offset += 4;
                }

                WaveformDataReceived?.Invoke(this, new WaveformDataEventArgs
                {
                    ChannelIndex = channelIndex,
                    Data = data,
                    SamplePeriod = samplePeriod
                });
            }
            catch { }
        }

        public static double DecodeValue(byte[] data, int offset, VariableType type)
        {
            return type switch
            {
                VariableType.UInt8 => data[offset],
                VariableType.Int8 => (sbyte)data[offset],
                VariableType.UInt16 => BitConverter.ToUInt16(data, offset),
                VariableType.Int16 => BitConverter.ToInt16(data, offset),
                VariableType.UInt32 => BitConverter.ToUInt32(data, offset),
                VariableType.Int32 => BitConverter.ToInt32(data, offset),
                VariableType.Float32 => BitConverter.ToSingle(data, offset),
                VariableType.Bool => data[offset] != 0 ? 1.0 : 0.0,
                VariableType.Logic => data[offset] != 0 ? 1.0 : 0.0,
                _ => 0
            };
        }

        public static byte[] EncodeValue(double value, VariableType type)
        {
            return type switch
            {
                VariableType.UInt8 => new[] { (byte)Math.Clamp(value, 0, 255) },
                VariableType.Int8 => new[] { (byte)(sbyte)Math.Clamp(value, -128, 127) },
                VariableType.UInt16 => BitConverter.GetBytes((ushort)Math.Clamp(value, 0, 65535)),
                VariableType.Int16 => BitConverter.GetBytes((short)Math.Clamp(value, -32768, 32767)),
                VariableType.UInt32 => BitConverter.GetBytes((uint)Math.Clamp(value, 0, uint.MaxValue)),
                VariableType.Int32 => BitConverter.GetBytes((int)Math.Clamp(value, int.MinValue, int.MaxValue)),
                VariableType.Float32 => BitConverter.GetBytes((float)value),
                VariableType.Bool => new[] { value != 0 ? (byte)1 : (byte)0 },
                VariableType.Logic => new[] { value != 0 ? (byte)1 : (byte)0 },
                _ => new byte[4]
            };
        }

        private static int GetTypeByteSize(VariableType type)
        {
            return type switch
            {
                VariableType.UInt8 or VariableType.Int8 or VariableType.Bool or VariableType.Logic => 1,
                VariableType.UInt16 or VariableType.Int16 => 2,
                VariableType.UInt32 or VariableType.Int32 or VariableType.Float32 => 4,
                _ => 4
            };
        }

        private VariableInfo? FindVariableByAnyName(string variableName)
        {
            return Variables.FirstOrDefault(v =>
                string.Equals(v.Name, variableName, StringComparison.Ordinal) ||
                string.Equals(v.DisplayName, variableName, StringComparison.Ordinal));
        }

        public bool TryResolveVariable(string variableName, out VariableInfo variable)
        {
            variable = FindVariableByAnyName(variableName) ?? new VariableInfo();
            return !string.IsNullOrEmpty(variable.Name);
        }

        public void RequestReadVariable(string variableName)
        {
            if (!TryResolveVariable(variableName, out var variable)) return;

            var nameBytes = System.Text.Encoding.ASCII.GetBytes(variable.Name);
            var payload = new byte[6 + nameBytes.Length];
            payload[0] = (byte)nameBytes.Length;
            Array.Copy(nameBytes, 0, payload, 1, nameBytes.Length);
            // address
            int offset = 1 + nameBytes.Length;
            BitConverter.GetBytes(variable.Address).CopyTo(payload, offset);
            payload[offset + 4] = (byte)variable.ModifiedType;

            _serial.SendCommand(IcsCommands.ReadVariable, payload);
        }

        public void RequestWriteVariable(string variableName, double value)
        {
            if (!TryResolveVariable(variableName, out var variable)) return;

            double rawValue = variable.Scale != 0 ? value / variable.Scale : value;
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(variable.Name);
            var valueBytes = EncodeValue(rawValue, variable.ModifiedType);

            var payload = new byte[2 + nameBytes.Length + 4 + valueBytes.Length];
            int offset = 0;
            payload[offset++] = (byte)nameBytes.Length;
            Array.Copy(nameBytes, 0, payload, offset, nameBytes.Length);
            offset += nameBytes.Length;
            BitConverter.GetBytes(variable.Address).CopyTo(payload, offset);
            offset += 4;
            payload[offset++] = (byte)variable.ModifiedType;
            Array.Copy(valueBytes, 0, payload, offset, valueBytes.Length);

            _serial.SendCommand(IcsCommands.WriteVariable, payload);
        }

        public void StartScope(double samplePeriod, int recordLength, IReadOnlyList<ScopeChannelRequest> channels)
        {
            if (channels == null || channels.Count == 0) return;

            int count = Math.Min(channels.Count, 12);
            var payload = new byte[9 + count * 6];
            BitConverter.GetBytes((float)samplePeriod).CopyTo(payload, 0);
            BitConverter.GetBytes(recordLength).CopyTo(payload, 4);
            payload[8] = (byte)count;

            int offset = 9;
            for (int i = 0; i < count; i++)
            {
                var channel = channels[i];
                payload[offset++] = (byte)Math.Clamp(channel.ChannelIndex, 0, 11);
                payload[offset++] = (byte)channel.Type;
                BitConverter.GetBytes(channel.Address).CopyTo(payload, offset);
                offset += 4;
            }

            _serial.SendCommand(IcsCommands.StartScope, payload);
        }

        public void StopScope()
        {
            _serial.SendCommand(IcsCommands.StopScope, Array.Empty<byte>());
        }

        public void SetTrigger(TriggerSettings trigger)
        {
            var payload = new byte[11];
            BitConverter.GetBytes((float)trigger.Position).CopyTo(payload, 0);
            BitConverter.GetBytes((float)trigger.Level).CopyTo(payload, 4);
            payload[8] = (byte)trigger.Source;
            payload[9] = (byte)trigger.Mode;
            payload[10] = (byte)trigger.Edge;
            _serial.SendCommand(IcsCommands.SetTrigger, payload);
        }

        public void RequestInfo()
        {
            _serial.SendCommand(IcsCommands.GetInfo, Array.Empty<byte>());
        }

        public void Dispose()
        {
            _serial.DataReceived -= OnDataReceived;
        }
    }
}
