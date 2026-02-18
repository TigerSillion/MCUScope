using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MCUScope.Models;
using MCUScope.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace MCUScope.ViewModels
{
    public partial class FocParameterItem : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _variableName = string.Empty;
        [ObservableProperty] private string _unit = string.Empty;
        [ObservableProperty] private string _group = string.Empty;
        [ObservableProperty] private double _value;
        [ObservableProperty] private double _defaultValue;
        [ObservableProperty] private bool _isModified;
        [ObservableProperty] private bool _isMapped;
        public VariableType VarType { get; set; } = VariableType.Float32;

        partial void OnValueChanged(double value)
        {
            IsModified = Math.Abs(value - DefaultValue) > 1e-9;
        }
    }

    public partial class FocDebugViewModel : ObservableObject
    {
        private readonly SessionState _session;

        public FocDebugViewModel()
        {
            _session = SessionState.Instance;
            _session.IcsService.VariableValueReceived += OnVariableValueReceived;
            InitializeParameters();
        }

        public ObservableCollection<FocParameterItem> Parameters { get; } = new();

        [ObservableProperty] private string _systemModeText = "STOP";
        [ObservableProperty] private int _mappedCount;
        [ObservableProperty] private int _totalCount;

        private void InitializeParameters()
        {
            // System Control
            Add("System Mode", "com_u1_system_mode", "", "System", VariableType.UInt8);
            Add("Write Enable", "com_u1_enable_write", "", "System", VariableType.UInt8);
            Add("UI Switch", "com_u1_sw_userif", "", "System", VariableType.UInt8);

            // Motor Parameters
            Add("Pole Pairs", "com_u2_mtr_pp", "", "Motor", VariableType.UInt16);
            Add("Resistance", "com_f4_mtr_r", "ohm", "Motor", VariableType.Float32);
            Add("Ld", "com_f4_mtr_ld", "H", "Motor", VariableType.Float32);
            Add("Lq", "com_f4_mtr_lq", "H", "Motor", VariableType.Float32);
            Add("Flux", "com_f4_mtr_m", "Wb", "Motor", VariableType.Float32);
            Add("Inertia", "com_f4_mtr_j", "kgm2", "Motor", VariableType.Float32);
            Add("Nominal Current", "com_f4_nominal_current_rms", "Arms", "Motor", VariableType.Float32);
            Add("Max Speed", "com_f4_max_speed_rpm", "rpm", "Motor", VariableType.Float32);

            // Timing
            Add("Offset Calc Time", "com_u2_offset_calc_time", "ms", "Timing", VariableType.UInt16);
            Add("Bootstrap Time", "com_u2_charge_bootstrap_time", "ms", "Timing", VariableType.UInt16);

            // Sensorless
            Add("Loop Mode", "com_u1_ctrl_loop_mode", "", "Sensorless", VariableType.UInt8);
            Add("OL Ref Id", "com_f4_ol_ref_id", "A", "Sensorless", VariableType.Float32);
            Add("Id Up Time", "com_f4_id_up_time", "s", "Sensorless", VariableType.Float32);
            Add("Id Down Time", "com_f4_id_down_time", "s", "Sensorless", VariableType.Float32);
            Add("Id Down Speed", "com_f4_id_down_speed_rpm", "rpm", "Sensorless", VariableType.Float32);
            Add("Id Up Speed", "com_f4_id_up_speed_rpm", "rpm", "Sensorless", VariableType.Float32);

            // Current Control
            Add("Current Omega", "com_f4_current_omega_hz", "Hz", "Current Ctrl", VariableType.Float32);
            Add("Current Zeta", "com_f4_current_zeta", "", "Current Ctrl", VariableType.Float32);

            // Speed Control
            Add("Speed Omega", "com_f4_speed_omega_hz", "Hz", "Speed Ctrl", VariableType.Float32);
            Add("Speed Zeta", "com_f4_speed_zeta", "", "Speed Ctrl", VariableType.Float32);
            Add("Speed LPF", "com_f4_speed_lpf_hz", "Hz", "Speed Ctrl", VariableType.Float32);
            Add("Ref Speed", "com_f4_ref_speed_rpm", "rpm", "Speed Ctrl", VariableType.Float32);
            Add("Speed Rate Limit", "com_f4_speed_rate_limit_rpm", "rpm/s", "Speed Ctrl", VariableType.Float32);
            Add("Overspeed Limit", "com_f4_overspeed_limit_rpm", "rpm", "Speed Ctrl", VariableType.Float32);

            // BEMF Observer
            Add("Observer Omega", "com_f4_e_obs_omega_hz", "Hz", "BEMF Observer", VariableType.Float32);
            Add("Observer Zeta", "com_f4_e_obs_zeta", "", "BEMF Observer", VariableType.Float32);
            Add("PLL Omega", "com_f4_pll_est_omega_hz", "Hz", "BEMF Observer", VariableType.Float32);
            Add("PLL Zeta", "com_f4_pll_est_zeta", "", "BEMF Observer", VariableType.Float32);

            // Sensorless Switch
            Add("Less Switch", "com_u1_flag_less_switch_use", "", "Less Switch", VariableType.UInt8);
            Add("Phase Error", "com_f4_switch_phase_err_deg", "deg", "Less Switch", VariableType.Float32);
            Add("Switch Time", "com_f4_opl2less_sw_time", "s", "Less Switch", VariableType.Float32);
            Add("Phase Err LPF", "com_f4_phase_err_lpf_cut_freq", "Hz", "Less Switch", VariableType.Float32);

            // Open-loop Damping
            Add("OL Damping", "com_u1_flag_openloop_damping_use", "", "OL Damping", VariableType.UInt8);
            Add("Ed HPF Omega", "com_f4_ed_hpf_omega", "Hz", "OL Damping", VariableType.Float32);
            Add("OL Damping Zeta", "com_f4_ol_damping_zeta", "", "OL Damping", VariableType.Float32);
            Add("FB Limit Rate", "com_f4_ol_damping_fb_limit_rate", "", "OL Damping", VariableType.Float32);

            // Optional Flags
            Add("Volt Err Comp", "com_u1_flag_volt_err_comp_use", "", "Options", VariableType.UInt8);
            Add("Flux Weakening", "com_u1_flag_fluxwkn_use", "", "Options", VariableType.UInt8);
            Add("MTPA", "com_u1_flag_mtpa_use", "", "Options", VariableType.UInt8);
            Add("Flying Start", "com_u1_flag_flying_start_use", "", "Options", VariableType.UInt8);
            Add("Stall Detection", "com_u1_flag_stall_detection_use", "", "Options", VariableType.UInt8);
            Add("Torque Vib Comp", "com_u1_flag_trq_vibration_comp_use", "", "Options", VariableType.UInt8);
            Add("Vib Comp Mode", "com_u1_flag_trq_vibration_comp_mode", "", "Options", VariableType.UInt8);

            // Stall Detection
            Add("Id HPF Time", "com_f4_id_hpf_time", "s", "Stall Detect", VariableType.Float32);
            Add("Iq HPF Time", "com_f4_iq_hpf_time", "s", "Stall Detect", VariableType.Float32);
            Add("Threshold Level", "com_f4_threshold_level", "A", "Stall Detect", VariableType.Float32);
            Add("Threshold Time", "com_f4_threshold_time", "s", "Stall Detect", VariableType.Float32);

            // Flying Start
            Add("Restart Speed", "com_f4_restart_speed", "rpm", "Flying Start", VariableType.Float32);
            Add("Off Time", "com_f4_off_time", "s", "Flying Start", VariableType.Float32);
            Add("Over Time", "com_f4_over_time", "s", "Flying Start", VariableType.Float32);
            Add("Brake Time", "com_f4_active_brake_time", "s", "Flying Start", VariableType.Float32);
            Add("Current Threshold", "com_f4_on_current_th", "A", "Flying Start", VariableType.Float32);

            // Torque Vibration
            Add("Target 2F", "com_u1_target_2f", "", "Torque Vib", VariableType.UInt8);
            Add("Time Lead 1F", "com_f4_timelead_1f", "", "Torque Vib", VariableType.Float32);
            Add("Time Lead 2F", "com_f4_timelead_2f", "", "Torque Vib", VariableType.Float32);
            Add("TF LPF Omega", "com_f4_tf_lpf_omega", "Hz", "Torque Vib", VariableType.Float32);
            Add("Output Gain 1F", "com_f4_output_gain_1f", "", "Torque Vib", VariableType.Float32);
            Add("Output Gain 2F", "com_f4_output_gain_2f", "", "Torque Vib", VariableType.Float32);
            Add("Input Weight 0", "com_f4_input_weight0", "", "Torque Vib", VariableType.Float32);
            Add("Input Weight 1", "com_f4_input_weight1", "", "Torque Vib", VariableType.Float32);
            Add("Input Weight 2", "com_f4_input_weight2", "", "Torque Vib", VariableType.Float32);
            Add("Suppress Th 1F", "com_f4_suppression_th_1f", "", "Torque Vib", VariableType.Float32);
            Add("Suppress Th 2F", "com_f4_suppression_th_2f", "", "Torque Vib", VariableType.Float32);
            Add("Abnormal Th 1F", "com_f4_abnormal_output_th_1f", "", "Torque Vib", VariableType.Float32);
            Add("Abnormal Th 2F", "com_f4_abnormal_output_th_2f", "", "Torque Vib", VariableType.Float32);

            TotalCount = Parameters.Count;
        }

        private void Add(string name, string varName, string unit, string group, VariableType type)
        {
            Parameters.Add(new FocParameterItem
            {
                Name = name,
                VariableName = varName,
                Unit = unit,
                Group = group,
                VarType = type
            });
        }

        /// <summary>
        /// Auto-discover FOC variables from loaded MAP/SYM file variables.
        /// </summary>
        [RelayCommand]
        private void AutoDiscover()
        {
            int mapped = 0;
            foreach (var param in Parameters)
            {
                param.IsMapped = _session.IcsService.TryResolveVariable(param.VariableName, out _);
                if (param.IsMapped) mapped++;
            }
            MappedCount = mapped;
            LogService.Info($"FOC AutoDiscover: {mapped}/{Parameters.Count} variables mapped");
        }

        [RelayCommand]
        private void ReadAll()
        {
            int count = 0;
            foreach (var param in Parameters)
            {
                if (param.IsMapped)
                {
                    _session.IcsService.RequestReadVariable(param.VariableName);
                    count++;
                }
            }
            LogService.Info($"FOC ReadAll: sent {count} read requests");
        }

        [RelayCommand]
        private void WriteAll()
        {
            int count = 0;
            foreach (var param in Parameters)
            {
                if (param.IsMapped && param.IsModified)
                {
                    _session.IcsService.RequestWriteVariable(param.VariableName, param.Value);
                    count++;
                }
            }
            LogService.Info($"FOC WriteAll: sent {count} write requests (modified only)");
        }

        [RelayCommand]
        private void ReadSelected(FocParameterItem? param)
        {
            if (param == null || !param.IsMapped) return;
            _session.IcsService.RequestReadVariable(param.VariableName);
        }

        [RelayCommand]
        private void WriteSelected(FocParameterItem? param)
        {
            if (param == null || !param.IsMapped) return;
            _session.IcsService.RequestWriteVariable(param.VariableName, param.Value);
            LogService.Info($"FOC Write: {param.VariableName} = {param.Value:G6}");
        }

        [RelayCommand]
        private void SystemStop()
        {
            var mode = Parameters.FirstOrDefault(p => p.VariableName == "com_u1_system_mode");
            if (mode != null && mode.IsMapped)
            {
                mode.Value = 0;
                _session.IcsService.RequestWriteVariable(mode.VariableName, 0);
                SystemModeText = "STOP";
                LogService.Info("FOC: System STOP");
            }
        }

        [RelayCommand]
        private void SystemRun()
        {
            var mode = Parameters.FirstOrDefault(p => p.VariableName == "com_u1_system_mode");
            if (mode != null && mode.IsMapped)
            {
                mode.Value = 1;
                _session.IcsService.RequestWriteVariable(mode.VariableName, 1);
                SystemModeText = "RUN";
                LogService.Info("FOC: System RUN");
            }
        }

        [RelayCommand]
        private void SystemReset()
        {
            var mode = Parameters.FirstOrDefault(p => p.VariableName == "com_u1_system_mode");
            if (mode != null && mode.IsMapped)
            {
                mode.Value = 2;
                _session.IcsService.RequestWriteVariable(mode.VariableName, 2);
                SystemModeText = "RESET";
                LogService.Info("FOC: System RESET");
            }
        }

        private void OnVariableValueReceived(object? sender, VariableReadEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var param = Parameters.FirstOrDefault(p =>
                    string.Equals(p.VariableName, e.VariableName, StringComparison.Ordinal));
                if (param != null)
                {
                    param.Value = e.Value;
                    param.DefaultValue = e.Value; // First read becomes default
                    param.IsModified = false;

                    if (param.VariableName == "com_u1_system_mode")
                    {
                        SystemModeText = e.Value switch
                        {
                            0 => "STOP",
                            1 => "RUN",
                            2 => "RESET",
                            _ => $"MODE({e.Value:F0})"
                        };
                    }
                }
            });
        }
    }
}
