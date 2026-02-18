using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace MCUScope.Services
{
    /// <summary>
    /// Short alias for XAML binding: {Binding [key], Source={x:Static svc:Loc.Instance}}
    /// </summary>
    public class Loc : INotifyPropertyChanged
    {
        private static Loc? _instance;
        public static Loc Instance => _instance ??= new Loc();

        public event PropertyChangedEventHandler? PropertyChanged;

        private Loc()
        {
            LocalizationService.Instance.LanguageChanged += () =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        public string this[string key] => LocalizationService.Instance[key];
    }

    public class LocalizationService : INotifyPropertyChanged
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? LanguageChanged;

        private string _currentLanguage = "en";
        private readonly Dictionary<string, Dictionary<string, string>> _strings = new();

        private LocalizationService()
        {
            LoadEnglish();
            LoadChinese();
        }

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage == value) return;
                _currentLanguage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageDisplayText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageDisplayCode)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
                LanguageChanged?.Invoke();
            }
        }

        public string LanguageDisplayText => _currentLanguage == "en" ? "EN" : "CN";

        public string this[string key]
        {
            get
            {
                if (_strings.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var val))
                    return val;
                if (_strings.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
                    return enVal;
                return key;
            }
        }

        public void ToggleLanguage()
        {
            CurrentLanguage = _currentLanguage == "en" ? "zh" : "en";
            // LanguageDisplayText depends on CurrentLanguage — notify after the setter fires
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageDisplayText)));
        }

        public string LanguageDisplayCode => _currentLanguage == "en" ? "EN" : "CN";

        private void LoadEnglish()
        {
            _strings["en"] = new Dictionary<string, string>
            {
                // Menu
                ["Menu.File"] = "_File",
                ["Menu.New"] = "_New",
                ["Menu.Open"] = "_Open",
                ["Menu.Save"] = "_Save",
                ["Menu.SaveAs"] = "Save _As",
                ["Menu.LoadVariables"] = "Load _Variables",
                ["Menu.UpdateVariables"] = "_Update Variables",
                ["Menu.SaveChartData"] = "Save _Chart Data",
                ["Menu.LoadChartData"] = "Load Chart _Data",
                ["Menu.Close"] = "_Close",
                ["Menu.Settings"] = "_Settings",
                ["Menu.VariableSettings"] = "_Variables Settings",
                ["Menu.CommunicationSettings"] = "_Communication Settings",
                ["Menu.Tools"] = "_Tools",
                ["Menu.ArrayEditor"] = "_Array Editor",
                ["Menu.CustomControlPanel"] = "_Custom Control Panel",
                ["Menu.TimetablePlayer"] = "_Timetable Player",
                ["Menu.Help"] = "_Help",
                ["Menu.About"] = "_About MCUScope",
                ["Menu.ManualCN"] = "Manual (CN)",

                // Toolbar
                ["Toolbar.Connect"] = "Connect",
                ["Toolbar.Disconnect"] = "Disconnect",
                ["Toolbar.UpdateVariables"] = "Update Variables",
                ["Toolbar.Refresh"] = "Refresh",
                ["Toolbar.Theme"] = "Theme:",
                ["Toolbar.BaudRate"] = "Baud:",

                // Watch
                ["Watch.Title"] = "Watch",
                ["Watch.Read"] = "Read",
                ["Watch.Write"] = "Write",
                ["Watch.AutoRead"] = "Auto Read",

                // Scope
                ["Scope.Controls"] = "Scope Controls",
                ["Scope.Run"] = "Run",
                ["Scope.Stop"] = "Stop",
                ["Scope.Time"] = "Time",
                ["Scope.Trigger"] = "Trigger",
                ["Scope.Channel"] = "Channel",
                ["Scope.Cursor"] = "Cursor",
                ["Scope.Zoom"] = "Zoom",
                ["Scope.Save"] = "Save",
                ["Scope.Load"] = "Load",
                ["Scope.FFT"] = "FFT",
                ["Scope.AutoSave"] = "Auto Save",
                ["Scope.Chart"] = "Scope Chart",

                // Labels
                ["Label.Mode"] = "Mode",
                ["Label.SecDiv"] = "Sec/Div",
                ["Label.Sample"] = "Sample",
                ["Label.Length"] = "Length",
                ["Label.Position"] = "Position",
                ["Label.Level"] = "Level",
                ["Label.Source"] = "Source",
                ["Label.Edge"] = "Edge",
                ["Label.Visible"] = "Visible",
                ["Label.Enabled"] = "Enabled",
                ["Label.Window"] = "Window",
                ["Label.Scale"] = "Scale",
                ["Label.Name"] = "Name",
                ["Label.Value"] = "Value",
                ["Label.Offset"] = "Offset",

                // Context menu
                ["Context.AddToScope"] = "Add to Scope Channel",
                ["Context.AddToWatch"] = "Add to Watch",
                ["Context.CopyName"] = "Copy Variable Name",
                ["Context.ReadValue"] = "Read Value",
                ["Context.RemoveFromWatch"] = "Remove from Watch",
                ["Context.CopyValue"] = "Copy Value",
                ["Context.RemoveChannel"] = "Remove Channel Binding",
                ["Context.SetTriggerSource"] = "Set as Trigger Source",
                ["Context.CopyChannelData"] = "Copy Channel Data",
                ["Context.ResetZoom"] = "Reset Zoom",
                ["Context.SaveScreenshot"] = "Save Screenshot",

                // Log
                ["Log.Output"] = "Output",
                ["Log.Clear"] = "Clear",

                // Variables
                ["Variables.Title"] = "Variables",

                // Status
                ["Status.Disconnected"] = "Disconnected",
                ["Status.Connected"] = "Connected",

                // FOC Debug
                ["FOC.Title"] = "FOC Debug",
                ["FOC.SystemControl"] = "System Control",
                ["FOC.MotorParams"] = "Motor Parameters",
                ["FOC.CurrentControl"] = "Current Control",
                ["FOC.SpeedControl"] = "Speed Control",
                ["FOC.BemfObserver"] = "BEMF Observer",
                ["FOC.SensorlessSwitch"] = "Sensorless Switch",
                ["FOC.OpenLoopDamping"] = "Open-loop Damping",
                ["FOC.OptionalFeatures"] = "Optional Features",
                ["FOC.StallDetection"] = "Stall Detection",
                ["FOC.FlyingStart"] = "Flying Start",
                ["FOC.TorqueVibration"] = "Torque Vibration",
                ["FOC.ReadAll"] = "Read All",
                ["FOC.WriteAll"] = "Write All",
                ["FOC.AutoDiscover"] = "Auto Discover",

                // Common
                ["Common.OK"] = "OK",
                ["Common.Cancel"] = "Cancel",
                ["Common.Apply"] = "Apply",
                ["Common.Error"] = "Error",
                ["Common.Success"] = "Success",
                ["Common.Warning"] = "Warning",
            };
        }

        private void LoadChinese()
        {
            _strings["zh"] = new Dictionary<string, string>
            {
                // Menu
                ["Menu.File"] = "文件(_F)",
                ["Menu.New"] = "新建(_N)",
                ["Menu.Open"] = "打开(_O)",
                ["Menu.Save"] = "保存(_S)",
                ["Menu.SaveAs"] = "另存为(_A)",
                ["Menu.LoadVariables"] = "加载变量(_V)",
                ["Menu.UpdateVariables"] = "更新变量(_U)",
                ["Menu.SaveChartData"] = "保存波形数据(_C)",
                ["Menu.LoadChartData"] = "加载波形数据(_D)",
                ["Menu.Close"] = "关闭(_C)",
                ["Menu.Settings"] = "设置(_S)",
                ["Menu.VariableSettings"] = "变量设置(_V)",
                ["Menu.CommunicationSettings"] = "通信设置(_C)",
                ["Menu.Tools"] = "工具(_T)",
                ["Menu.ArrayEditor"] = "数组编辑器(_A)",
                ["Menu.CustomControlPanel"] = "自定义控制面板(_C)",
                ["Menu.TimetablePlayer"] = "时序播放器(_T)",
                ["Menu.Help"] = "帮助(_H)",
                ["Menu.About"] = "关于 MCUScope(_A)",
                ["Menu.ManualCN"] = "使用说明（中文）",

                // Toolbar
                ["Toolbar.Connect"] = "连接",
                ["Toolbar.Disconnect"] = "断开",
                ["Toolbar.UpdateVariables"] = "更新变量",
                ["Toolbar.Refresh"] = "刷新",
                ["Toolbar.Theme"] = "主题:",
                ["Toolbar.BaudRate"] = "波特率:",

                // Watch
                ["Watch.Title"] = "变量监视",
                ["Watch.Read"] = "读取",
                ["Watch.Write"] = "写入",
                ["Watch.AutoRead"] = "自动读取",

                // Scope
                ["Scope.Controls"] = "示波器控制",
                ["Scope.Run"] = "运行",
                ["Scope.Stop"] = "停止",
                ["Scope.Time"] = "时基",
                ["Scope.Trigger"] = "触发",
                ["Scope.Channel"] = "通道",
                ["Scope.Cursor"] = "光标",
                ["Scope.Zoom"] = "缩放",
                ["Scope.Save"] = "保存",
                ["Scope.Load"] = "加载",
                ["Scope.FFT"] = "FFT",
                ["Scope.AutoSave"] = "自动保存",
                ["Scope.Chart"] = "示波器波形",

                // Labels
                ["Label.Mode"] = "模式",
                ["Label.SecDiv"] = "秒/格",
                ["Label.Sample"] = "采样",
                ["Label.Length"] = "长度",
                ["Label.Position"] = "位置",
                ["Label.Level"] = "电平",
                ["Label.Source"] = "源",
                ["Label.Edge"] = "边沿",
                ["Label.Visible"] = "可见",
                ["Label.Enabled"] = "启用",
                ["Label.Window"] = "窗函数",
                ["Label.Scale"] = "刻度",
                ["Label.Name"] = "名称",
                ["Label.Value"] = "值",
                ["Label.Offset"] = "偏移",

                // Context menu
                ["Context.AddToScope"] = "添加到示波器通道",
                ["Context.AddToWatch"] = "添加到变量监视",
                ["Context.CopyName"] = "复制变量名",
                ["Context.ReadValue"] = "读取值",
                ["Context.RemoveFromWatch"] = "从监视移除",
                ["Context.CopyValue"] = "复制值",
                ["Context.RemoveChannel"] = "移除通道绑定",
                ["Context.SetTriggerSource"] = "设为触发源",
                ["Context.CopyChannelData"] = "复制通道数据",
                ["Context.ResetZoom"] = "重置缩放",
                ["Context.SaveScreenshot"] = "保存截图",

                // Log
                ["Log.Output"] = "输出",
                ["Log.Clear"] = "清空",

                // Variables
                ["Variables.Title"] = "变量列表",

                // Status
                ["Status.Disconnected"] = "已断开",
                ["Status.Connected"] = "已连接",

                // FOC Debug
                ["FOC.Title"] = "FOC调试",
                ["FOC.SystemControl"] = "系统控制",
                ["FOC.MotorParams"] = "电机参数",
                ["FOC.CurrentControl"] = "电流环控制",
                ["FOC.SpeedControl"] = "速度环控制",
                ["FOC.BemfObserver"] = "BEMF观测器",
                ["FOC.SensorlessSwitch"] = "无感切换",
                ["FOC.OpenLoopDamping"] = "开环阻尼",
                ["FOC.OptionalFeatures"] = "可选功能",
                ["FOC.StallDetection"] = "堵转检测",
                ["FOC.FlyingStart"] = "飞车启动",
                ["FOC.TorqueVibration"] = "转矩振动",
                ["FOC.ReadAll"] = "全部读取",
                ["FOC.WriteAll"] = "全部写入",
                ["FOC.AutoDiscover"] = "自动发现",

                // Common
                ["Common.OK"] = "确定",
                ["Common.Cancel"] = "取消",
                ["Common.Apply"] = "应用",
                ["Common.Error"] = "错误",
                ["Common.Success"] = "成功",
                ["Common.Warning"] = "警告",
            };
        }
    }
}
