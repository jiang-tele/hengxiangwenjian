using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.Statistics;
using Microsoft.VisualBasic;
using NAudio.Wave;
using NModbus.Device;
using S7.Net;
using Sunny.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using Timer = System.Threading.Timer;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;
using Document = DocumentFormat.OpenXml.Wordprocessing.Document;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;
using Table = DocumentFormat.OpenXml.Wordprocessing.Table;
using RunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;
using Run = DocumentFormat.OpenXml.Wordprocessing.Run;
using Bold = DocumentFormat.OpenXml.Wordprocessing.Bold;
using TopBorder = DocumentFormat.OpenXml.Wordprocessing.TopBorder;
using RightBorder = DocumentFormat.OpenXml.Wordprocessing.RightBorder;
using LeftBorder = DocumentFormat.OpenXml.Wordprocessing.LeftBorder;
using BottomBorder = DocumentFormat.OpenXml.Wordprocessing.BottomBorder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;




namespace 阴极辊控制与数据处理
{
    public partial class Form1 : UIForm
    {
        private RollHistoryData? _currentHistoryData;
        private readonly float _designWidth;   // 原始窗体宽度
        private readonly float _designHeight;  // 原始窗体高度

        // ========== 新增：保存动态生成的按钮，方便清空
        private List<UIButton> _dynamicButtons = new List<UIButton>();
        // ========== 新增：后台线程+取消令牌（核心控制变量） ==========
        private Thread? _collectThread; // 自动采集后台线程
        private CancellationTokenSource? _cts; // 取消令牌（用于中途停止）
        private bool _isStopping = false; // 新增：是否正在停止中
        private bool _stopNow = false;
        // 最近一次自动采集目录（仅「结果显示」热力图使用）
        private string? _lastCollectFolder;
        private int _lastMatrixRows;
        private int _lastMatrixCols;
        public Form1()
        {
            InitializeComponent();
            this.AutoScaleMode = AutoScaleMode.None; // 等比放大
            writer = null;
            waveIn = null;
            serialPort = null;
            modbus = null;
            connectionTimer = null;
            outputFilePath = null;
            // ==================== 缩放初始化（新增） ====================
            _designWidth = Width;
            _designHeight = Height;
            SetTag(this); // 记录所有控件初始大小


            // 3.基础音频文件夹路径
            string baseAudioFolder = @"D:\音频";
            Directory.CreateDirectory(baseAudioFolder); // 确保基础文件夹存在

            // 必须在构造函数绑定（不能仅依赖 Form1_Load / 设计器，否则点击可能无任何反应）
            WireResultDisplayButton();
            WireManualPlcButtons();
        }

        /// <summary>PLC 已连接且队列可用（手动/自动写寄存器前调用）。</summary>
        private bool EnsurePlcReady(string actionName, bool silent = false)
        {
            if (!isConnected || modbus == null)
            {
                if (!silent) Log($"请先连接PLC（{actionName}）");
                return false;
            }
            if (_modbusQueue == null)
            {
                if (!silent) Log($"Modbus 通信未就绪，请重新点击连接（{actionName}）");
                return false;
            }
            return true;
        }

        private async Task WritePlcInt32Async(int address, int value)
        {
            await _modbusQueue!.WriteInt32Async(1, address, value);
        }

        /// <summary>绑定手动平台方向键（uiSymbolButton1~6），防止设计器事件丢失。</summary>
        private void WireManualPlcButtons()
        {
            void Bind(Sunny.UI.UISymbolButton? btn,
                MouseEventHandler down, MouseEventHandler up)
            {
                if (btn == null) return;
                btn.Enabled = true;
                btn.MouseDown -= down;
                btn.MouseDown += down;
                btn.MouseUp -= up;
                btn.MouseUp += up;
            }

            Bind(uiSymbolButton1, uiSymbolButton1_MouseDown, uiSymbolButton1_MouseUp);
            Bind(uiSymbolButton2, uiSymbolButton2_MouseDown, uiSymbolButton2_MouseUp);
            Bind(uiSymbolButton3, uiSymbolButton3_MouseDown, uiSymbolButton3_MouseUp);
            Bind(uiSymbolButton4, uiSymbolButton4_MouseDown, uiSymbolButton4_MouseUp);
            Bind(uiSymbolButton5, uiSymbolButton5_MouseDown, uiSymbolButton5_MouseUp);
            Bind(uiSymbolButton6, uiSymbolButton6_MouseDown, uiSymbolButton6_MouseUp);
        }

        /// <summary>绑定「结果显示」按钮 → 热力图逻辑（按 Name 查找，避免设计器漏绑）。</summary>
        private void WireResultDisplayButton()
        {
            Sunny.UI.UIButton? btn = 结果显示;
            if (btn == null)
            {
                btn = Controls.Find("结果显示", true)
                    .OfType<Sunny.UI.UIButton>()
                    .FirstOrDefault();
            }
            if (btn == null)
            {
                System.Diagnostics.Debug.WriteLine("WireResultDisplayButton: 未找到「结果显示」按钮");
                return;
            }
            btn.Enabled = true;
            btn.Visible = true;
            btn.Click -= 结果显示_Click;
            btn.Click += 结果显示_Click;
            System.Diagnostics.Debug.WriteLine("WireResultDisplayButton: 已绑定 Click → 结果显示_Click");
        }
        #region ModbusMaster定义
        /// <summary>
        /// Modbus RTU 主站类，通过串口与从站通信
        /// 实现了 IDisposable 接口，便于资源释放（当前仅作标记，可扩展）
        /// </summary>
        public class ModbusMaster : IDisposable
        {
            // 串口对象，用于与 Modbus 从站通信
            private readonly SerialPort serialPort;
            // 锁对象，保证多线程环境下串口操作的原子性
            private readonly object lockObj = new object();

            /// <summary>
            /// 构造函数，接收一个已配置的 SerialPort 对象
            /// </summary>
            /// <param name="port">已打开的串口实例（通常已在外部设置好波特率、数据位等参数）</param>
            public ModbusMaster(SerialPort port)
            {
                serialPort = port;
                // 设置读写超时，避免无限阻塞
                serialPort.ReadTimeout = 1000;
                serialPort.WriteTimeout = 1000;
            }

            #region CRC16 计算（Modbus 标准）
            /// <summary>
            /// 计算 Modbus RTU 的 CRC16 校验码
            /// 多项式：0x8005（实际使用反转形式 0xA001）
            /// </summary>
            /// <param name="data">待计算的数据字节数组（不包含 CRC 本身）</param>
            /// <returns>2 字节 CRC 值，低字节在前（符合 Modbus 传输顺序）</returns>
            private byte[] CalculateCRC(byte[] data)
            {
                ushort crc = 0xFFFF;               // 初始值
                for (int i = 0; i < data.Length; i++)
                {
                    crc ^= data[i];                 // 与当前字节异或
                    for (int j = 0; j < 8; j++)      // 处理 8 个位
                    {
                        if ((crc & 0x0001) != 0)      // 如果最低位为 1
                        {
                            crc >>= 1;                 // 右移一位
                            crc ^= 0xA001;              // 与多项式反转值 0xA001 异或
                        }
                        else
                        {
                            crc >>= 1;                  // 仅右移
                        }
                    }
                }
                // 返回低字节在前（例如 CRC=0x1234，则返回 [0x34, 0x12]）
                return BitConverter.GetBytes(crc);
            }
            #endregion

            #region 核心事务处理
            /// <summary>
            /// 执行 Modbus 请求-响应事务（线程安全）
            /// 发送请求帧，接收响应，并进行基本校验（地址、CRC、异常码）
            /// </summary>
            /// <param name="request">完整的请求帧（包含 CRC）</param>
            /// <returns>完整的响应帧（包含 CRC）</returns>
            /// <exception cref="Exception">超时、数据不完整、地址不符、CRC 错误或从站返回异常时抛出</exception>
            private byte[] ExecuteTransaction(byte[] request)
            {
                lock (lockObj)                         // 确保同一时刻只有一个线程操作串口
                {
                    // 清空串口缓冲区，避免残留数据干扰
                    serialPort.DiscardInBuffer();
                    serialPort.DiscardOutBuffer();

                    // 发送请求帧
                    serialPort.Write(request, 0, request.Length);
                    Thread.Sleep(50);                   // 等待从站响应（简单延时，可根据实际情况调整或使用更精确的超时机制）

                    // 接收响应数据
                    List<byte> response = new List<byte>();
                    int startTime = Environment.TickCount;          // 获取当前系统时间（毫秒）
                    while (Environment.TickCount - startTime < 200) // 超时 200ms
                    {
                        if (serialPort.BytesToRead > 0)              // 有数据可读
                        {
                            byte b = (byte)serialPort.ReadByte();    // 读取一个字节
                            response.Add(b);
                        }
                        else
                        {
                            Thread.Sleep(10);                         // 无数据则短暂等待，避免空转
                        }
                    }

                    // 检查响应最小长度：地址(1) + 功能码(1) + 数据(至少1) + CRC(2) = 5
                    //if (response.Count < 5)
                    //    throw new Exception("响应超时或长度不足");

                    //// 校验从站地址是否与请求一致
                    //if (response[0] != request[0])
                    //    throw new Exception("从站地址不匹配");

                    //// 提取响应中的 CRC 并验证
                    //byte[] receivedCrc = response.Skip(response.Count - 2).Take(2).ToArray();   // 最后两字节
                    //byte[] calcCrc = CalculateCRC(response.Take(response.Count - 2).ToArray()); // 对数据部分重新计算 CRC
                    //if (receivedCrc[0] != calcCrc[0] || receivedCrc[1] != calcCrc[1])
                    //    throw new Exception("CRC校验失败");

                    //// 检查是否为 Modbus 异常响应（功能码的最高位为 1）
                    //if ((response[1] & 0x80) != 0)
                    //    throw new Exception($"Modbus异常码：{response[2]}"); // 异常码位于数据域第一字节

                    return response.ToArray();
                }
            }
            #endregion





            #region 功能码 03：读保持寄存器
            /// <summary>
            /// 读取连续的保持寄存器值（每个寄存器 16 位，功能码 03）
            /// </summary>
            /// <param name="slaveId">从站地址</param>
            /// <param name="plcAddress">PLC 寄存器起始地址（如 40001）</param>
            /// <param name="numRegisters">要读取的寄存器数量（最大值受从站限制，通常不超过 125）</param>
            /// <returns>ushort 数组，长度为 numRegisters，顺序与请求地址对应</returns>
            public ushort[] ReadHoldingRegisters(byte slaveId, int plcAddress, ushort numRegisters)
            {
                ushort protocolAddress = (ushort)(plcAddress - 2);

                // 请求帧：地址 + 功能码03 + 起始地址(2) + 寄存器数量(2) + CRC(2)
                byte[] request = new byte[8];
                request[0] = slaveId;
                request[1] = 0x03;
                request[2] = (byte)(protocolAddress >> 8);
                request[3] = (byte)protocolAddress;
                request[4] = (byte)(numRegisters >> 8);        // 寄存器数量高字节
                request[5] = (byte)numRegisters;                // 寄存器数量低字节
                byte[] crc = CalculateCRC(request.Take(6).ToArray());
                request[6] = crc[0];
                request[7] = crc[1];

                byte[] response = ExecuteTransaction(request);

                // 响应格式：[地址, 功能码, 字节数(byteCount), 数据(byteCount字节), CRC...]
                int byteCount = response[2];                     // 数据域字节数 = 寄存器数 × 2
                if (byteCount != numRegisters * 2)
                    throw new Exception("寄存器数量不匹配");

                // 解析数据：每个寄存器高字节在前，低字节在后
                ushort[] registers = new ushort[numRegisters];
                for (int i = 0; i < numRegisters; i++)
                {
                    registers[i] = (ushort)((response[3 + i * 2] << 8) | response[4 + i * 2]);
                }
                return registers;
            }
            #endregion

            #region 功能码 16：写多个寄存器
            /// <summary>
            /// 写入连续的多个寄存器（功能码 16）
            /// </summary>
            /// <param name="slaveId">从站地址</param>
            /// <param name="plcAddress">PLC 寄存器起始地址</param>
            /// <param name="values">要写入的 ushort 数组，长度即为寄存器数量</param>
            public void WriteMultipleRegisters(byte slaveId, int plcAddress, ushort[] values)
            {
                ushort protocolAddress = (ushort)(plcAddress - 2);
                byte numRegisters = (byte)values.Length;          // 寄存器数量（注意：实际 Modbus 允许最大 0x007B，这里简化使用 byte）
                byte byteCount = (byte)(numRegisters * 2);        // 数据字节数

                // 请求帧长度 = 地址(1) + 功能码(1) + 起始地址(2) + 寄存器数量(2) + 字节数(1) + 数据(byteCount) + CRC(2)
                byte[] request = new byte[9 + byteCount];
                request[0] = slaveId;
                request[1] = 0x10;                                 // 功能码 16 (0x10)
                request[2] = (byte)(protocolAddress >> 8);
                request[3] = (byte)protocolAddress;
                request[4] = (byte)(numRegisters >> 8);            // 寄存器数量高字节（通常为0）
                request[5] = (byte)numRegisters;                    // 寄存器数量低字节
                request[6] = byteCount;                             // 后续数据字节数
                                                                    // 填充寄存器数据：每个寄存器高字节在前，低字节在后
                for (int i = 0; i < numRegisters; i++)
                {
                    request[7 + i * 2] = (byte)(values[i] >> 8);   // 高字节
                    request[8 + i * 2] = (byte)values[i];          // 低字节
                }
                // 计算 CRC（从索引0到倒数第三字节，即除 CRC 外的所有数据）
                byte[] crc = CalculateCRC(request.Take(request.Length - 2).ToArray());
                request[request.Length - 2] = crc[0];               // CRC 低字节
                request[request.Length - 1] = crc[1];               // CRC 高字节

                // 执行事务，正常响应会回显地址、功能码、起始地址和寄存器数量
                ExecuteTransaction(request);
            }
            #endregion





            /// <summary>
            /// 读取一个 32 位有符号整数（占用两个连续的保持寄存器）
            /// </summary>
            /// <param name="slaveId">从站地址</param>
            /// <param name="plcAddress">PLC 起始地址（如 40001，将读取 40001-40002）</param>
            /// <returns>int 值</returns>
            public int ReadInt32(byte slaveId, int plcAddress)
            {
                // 读取两个寄存器
                ushort[] registers = ReadHoldingRegisters(slaveId, plcAddress, 2);

                // 将两个寄存器组合成 32 位无符号整数（寄存器0为高16位，寄存器1为低16位）
                // 注意：移位操作与系统字节序无关，直接得到正确的数值
                uint raw = (uint)((registers[0] << 16) | registers[1]);

                // 将无符号整数按位转换为有符号整数（C# 默认 unchecked，位模式不变）
                return (int)raw;
            }

            /// <summary>
            /// 写入一个 32 位有符号整数到两个连续的保持寄存器
            /// </summary>
            /// <param name="slaveId">从站地址</param>
            /// <param name="plcAddress">PLC 起始地址</param>
            /// <param name="value">要写入的 int 值</param>
            public void WriteInt32(byte slaveId, int plcAddress, int value)
            {
                // 将有符号整数视为无符号，便于位操作
                uint u = (uint)value;

                // 拆分为两个 16 位值：高16位和低16位
                ushort[] registers = new ushort[2];
                registers[0] = (ushort)(u >> 16);   // 高16位（对应第一个寄存器）
                registers[1] = (ushort)(u & 0xFFFF); // 低16位（对应第二个寄存器）

                // 调用已有的写多个寄存器方法
                WriteMultipleRegisters(slaveId, plcAddress, registers);
            }

            #region IDisposable 实现
            /// <summary>
            /// 释放资源
            /// </summary>
            public void Dispose()
            {
                serialPort?.Dispose();
            }
            #endregion
        }
        #endregion

        #region modbus异步处理
        // ========== Modbus 请求队列调度器（解决冲突） ==========
        private ModbusRequestQueue? _modbusQueue;
        private class ModbusRequestQueue
        {
            private readonly BlockingCollection<Func<Task>> _queue = new();
            private readonly ModbusMaster _modbus;
            private readonly int _interFrameDelayMs;

            private readonly Action<string> _logger;
            public ModbusRequestQueue(ModbusMaster modbus, int interFrameDelayMs, Action<string> logger)
            {
                _modbus = modbus;
                _interFrameDelayMs = interFrameDelayMs;
                _logger = logger;
                Task.Run(ProcessQueue);
            }

            private async Task ProcessQueue()
            {
                foreach (var taskFunc in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        await taskFunc();
                        await Task.Delay(_interFrameDelayMs);
                    }
                    catch (Exception ex)
                    {
                        try { _logger?.Invoke($"Modbus队列错误: {ex.Message}"); }
                        catch { Debug.WriteLine($"Modbus队列错误: {ex.Message}"); }
                    }
                }
            }

            // 异步读取 Int32（返回 Task<int>，可 await）
            public Task<int> ReadInt32Async(byte slaveId, int address)
            {
                var tcs = new TaskCompletionSource<int>();
                _queue.Add(() =>
                {
                    try
                    {
                        int val = _modbus.ReadInt32(slaveId, address);
                        tcs.SetResult(val);
                    }
                    catch (Exception ex) { tcs.SetException(ex); }
                    return Task.CompletedTask;
                });
                return tcs.Task;
            }

            // 异步写入 Int32（返回 Task，可 await）
            public Task WriteInt32Async(byte slaveId, int address, int value)
            {
                var tcs = new TaskCompletionSource<bool>();
                _queue.Add(() =>
                {
                    try
                    {
                        _modbus.WriteInt32(slaveId, address, value);
                        tcs.SetResult(true);
                    }
                    catch (Exception ex) { tcs.SetException(ex); }
                    return Task.CompletedTask;
                });
                return tcs.Task;
            }

            public void Dispose() => _queue.CompleteAdding();
        }


        #endregion

        #region 全局控件等比缩放核心方法
        /// <summary>
        /// 递归给所有控件记录初始坐标、尺寸，存在Tag
        /// </summary>
        private void SetTag(System.Windows.Forms.Control control)
        {
            foreach (System.Windows.Forms.Control c in control.Controls)
            {
                c.Tag = new Rectangle(c.Left, c.Top, c.Width, c.Height);
                SetTag(c);
            }
        }

        /// <summary>
        /// 窗体大小改变时，所有控件等比缩放适配
        /// </summary>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_designWidth == 0 || _designHeight == 0) return;

            float scaleX = this.Width / _designWidth;
            float scaleY = this.Height / _designHeight;

            ScaleControl(this, scaleX, scaleY);
        }

        /// <summary>
        /// 递归缩放控件位置和大小
        /// </summary>
        private void ScaleControl(System.Windows.Forms.Control control, float scaleX, float scaleY)
        {
            foreach (System.Windows.Forms.Control c in control.Controls)
            {
                if (c.Tag is Rectangle rect)
                {
                    c.Left = (int)(rect.X * scaleX);
                    c.Top = (int)(rect.Y * scaleY);
                    c.Width = (int)(rect.Width * scaleX);
                    c.Height = (int)(rect.Height * scaleY);
                }
                ScaleControl(c, scaleX, scaleY);
            }

            foreach (var btn in _dynamicButtons)
            {
                if (btn.Tag is Rectangle r)
                {
                    btn.Left = (int)(r.X * scaleX);
                    btn.Top = (int)(r.Y * scaleY);
                    btn.Width = (int)(r.Width * scaleX);
                    btn.Height = (int)(r.Height * scaleY);
                }
            }
        }
        #endregion
        #region 记录程序操作日志方法编写
        public async void Log(string message)
        {
            // 获取当前时间并转换为字符串
            string Time = Convert.ToString(DateTime.Now);

            // 将时间和日志信息添加到信息广播器
            uiListBox1.Items.Add(Time + "  " + message + "\n");

            // 选中最后一项，以确保最新添加的项可见
            uiListBox1.SelectedIndex = uiListBox1.Items.Count - 1;

            // 取消选中，以防止用户误操作
            uiListBox1.SelectedIndex = -1;
        }
        #endregion


        #region plc连接与错误复位代码   
        private SerialPort serialPort;
        private ModbusMaster modbus;
        private Timer connectionTimer;
        private bool isConnected = false;
        private System.Windows.Forms.Timer timer; // 使用 WinForms 专用 Timer
        private void uiButton1_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                try
                {
                    serialPort = new SerialPort("COM3", 9600, Parity.None, 8, StopBits.One);
                    serialPort.Open();
                    modbus = new ModbusMaster(serialPort);
                    isConnected = true;
                    Log("PLC已连接");
                    uiLabel7.Text = "已连接";
                    uiButton1.BackColor = System.Drawing.Color.Red;
                    _modbusQueue = new ModbusRequestQueue(modbus, 30, Log);
                    // 创建并配置 Timer
                    timer = new System.Windows.Forms.Timer();
                    timer.Interval = 1000; // 间隔 1000 毫秒 = 1 秒
                    timer.Tick += Timer_Tick;
                    timer.Start();         // 启动计时器
                }
                catch (Exception ex)
                {
                    Log($"连接失败：{ex.Message}");
                }
            }
            else
            {
                DisconnectPlc("已断开连接");
            }
        }

        private void DisconnectPlc(string logMessage)
        {
            timer?.Stop();
            _modbusQueue?.Dispose();
            _modbusQueue = null;
            connectionTimer?.Dispose();
            modbus?.Dispose();
            modbus = null;
            if (serialPort != null && serialPort.IsOpen)
                serialPort.Close();
            serialPort?.Dispose();
            serialPort = null;
            isConnected = false;
            Log(logMessage);
            uiLabel7.Text = "已断开";
            uiLabel7.ForeColor = System.Drawing.Color.Yellow;
        }
        private async void Timer_Tick(object sender, EventArgs e)
        {
            if (_modbusQueue == null) return;
            try
            {
                int value1 = await _modbusQueue.ReadInt32Async(1, 409);
                int value2 = await _modbusQueue.ReadInt32Async(1, 405);

                // UI 更新需在主线程
                uiTextBox1.Text = (value1 / 100.0).ToString();
                uiTextBox2.Text = (value2 / 100.0).ToString();
            }
            catch (Exception ex)
            {
                Log($"定时读取失败：{ex.Message}");
            }

        }


        private void uiButton3_Click(object sender, EventArgs e)
        {
            if (!isConnected) return;
            DisconnectPlc("连接已断开");
        }


        private async void uiButton2_MouseDown(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("故障复位")) return;
            try
            {

                await WritePlcInt32Async(66, 1);
                Log("发送 True 到 故障复位");
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiButton2_MouseUp(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("故障复位")) return;
            try
            {

                await WritePlcInt32Async(66, 0);
                Log("发送 False 到 故障复位");
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        #endregion

        #region ==================== 导出 Word 报告（极简、零报错） ====================
        private void uiButton_ExportReport_Click(object sender, EventArgs e)
        {
            MessageBox.Show("触发导出按钮事件");
            using (InputForm inputForm = new InputForm())
            {
                inputForm.ShowDialog(this);
            }
        }
        #endregion
        #region 手动控制代码


        private async void uiSymbolButton6_MouseDown(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(50, 1);
                // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiSymbolButton6_MouseUp(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(50, 0);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiSymbolButton4_MouseDown(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(51, 1);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiSymbolButton4_MouseUp(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(51, 0);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }


        private async void uiSymbolButton2_MouseDown(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(60, 1);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiSymbolButton2_MouseUp(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(60, 0);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiSymbolButton1_MouseDown(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(61, 1);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiSymbolButton1_MouseUp(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(61, 0);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiSymbolButton5_MouseDown(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(55, 1);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiSymbolButton5_MouseUp(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(55, 0);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiSymbolButton3_MouseDown(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(56, 1);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private async void uiSymbolButton3_MouseUp(object sender, MouseEventArgs e)
        {
            if (!EnsurePlcReady("平台移动")) return;
            try
            {

                await WritePlcInt32Async(56, 0);
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }
        #endregion


        #region 自动控制代码


        #region 自动采集按钮
        // 带取消令牌的 Modbus 读取，防止线程卡死
        private int ReadInt32WithCancel(byte slaveId, int address, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested(); // 每一次读之前都检查停止

                try
                {
                    return modbus.ReadInt32(slaveId, address);
                }
                catch
                {
                    token.ThrowIfCancellationRequested();
                    Thread.Sleep(50);
                }
            }
        }
        private List<WaveInCapabilities> microphones = new List<WaveInCapabilities>();
        private WaveInEvent waveIn;
        private WaveFileWriter writer;
        private string outputFilePath;
        // ==================== 停止采集按钮 ====================
        private void uiButtonStop_Click(object sender, EventArgs e)
        {
            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();       // 发送停止信号
                    Log("自动采集已停止");
                }
                else
                {
                    MessageBox.Show("未启动采集，无需停止");
                }
            }
            catch (Exception ex)
            {
                Log($"停止失败：{ex.Message}");
            }
        }
        #region 历史数据导入后的刷新方法（避免CS0103错误）
        /// <summary>刷新结果波形图（pictureBox2）</summary>
        private void RefreshResultPicture()
        {
            // 目前仅占位，后续可扩展波形绘制逻辑
            Log("历史数据已导入，可点击「结果显示」查看热力图");
        }

        /// <summary>刷新云图画布（pictureBox1）</summary>
        private void RefreshCloudPicture()
        {
            // 目前仅占位，后续可扩展云图绘制逻辑
            Log("云图刷新占位：可点击「云图生成」按钮生成");
        }
        #endregion

        private void uiButton7_Click(object sender, EventArgs e)
        {
            // 1. 读取输入参数
            if (!int.TryParse(uiTextBox4.Text, out int muxian) || muxian <= 0)
            {
                MessageBox.Show("请输入有效的母线数量（大于0）");
                return;
            }
            if (!int.TryParse(uiTextBox6.Text, out int totalLength) || totalLength <= 0)
            {
                MessageBox.Show("请输入有效的总长度（大于0）");
                return;
            }
            //if (!int.TryParse(uiTextBox5.Text, out int step) || step <= 0)
            //{
            //    MessageBox.Show("请输入有效的间隔（大于0）");
            //    return;
            //}

            //// 2. 计算相关参数
            //int caijidian = (int)(totalLength / step);           // 每条母线的采集点数
            //if (caijidian < 1) caijidian = 1;
            //int rotateAngle = 360 / muxian;                   // 每条母线之间的旋转角度
            // 2. 计算相关参数（新版：总长度 ÷ 采集点数 = 间隔）
            int caijidian;

            // 从输入框读取“采集点数”（你原来用的是 uiTextBox5 输入间隔，现在输入采集点数）
            if (!int.TryParse(uiTextBox5.Text, out caijidian) || caijidian <= 0)
            {
                Log("请输入有效的采集点数（大于0）");
                return;
            }
            // 读取新增的两侧密集采集参数
            if (!int.TryParse(uiTextBox10.Text, out int sideLength) || sideLength < 0)
            {
                Log("错误：请输入有效的两侧采集长度（非负整数）");
                return;
            }
            if (!int.TryParse(uiTextBox11.Text, out int sideTotalPoints) || sideTotalPoints < 0)
            {
                Log("错误：请输入有效的两侧采集点数（非负整数）");
                return;
            }

            // 校验：两侧总点数不能超过总采集点数
            if (sideTotalPoints > caijidian)
            {
                Log("错误：两侧采集点数不能超过总采集点数");
                return;
            }
            // 校验：两侧长度不能超过辊幅宽的一半
            if (sideLength * 2 > totalLength)
            {
                Log("错误：两侧采集长度之和不能超过辊幅宽");
                return;
            }

            //// 计算间隔（保留2位小数）
            //double stepDouble = (double)totalLength / (caijidian - 1);
            //stepDouble = Math.Round(stepDouble, 2);

            //// ×100 取整（传给PLC用）
            //int step = (int)Math.Round(stepDouble * 100);
            // ==================== 核心：变间距采集位置计算（两侧密集、中间稀疏） ====================
            List<int> moveSteps = new List<int>(); // 每一步要移动的距离（×100 给PLC用）
            List<double> positions = new List<double>(); // 所有采集点的位置坐标（单位：mm）

            // 1. 分解参数
            int oneSidePoints = sideTotalPoints / 2;  // 单侧点数（左右对称）
            int middlePoints = caijidian - sideTotalPoints; // 中间稀疏部分的点数

            // 2. 计算各段的步长
            double sideStep = oneSidePoints > 0 ? (double)sideLength / oneSidePoints : 0; // 两侧密集步长
            double middleLength = totalLength - 2 * sideLength; // 中间稀疏部分的总长度
            double middleStep = middlePoints > 1 ? middleLength / (middlePoints - 1) : middleLength; // 中间稀疏步长

            // 3. 生成所有采集点的位置（单位：mm）
            // 左侧密集段
            for (int i = 0; i < oneSidePoints; i++)
            {
                positions.Add(i * sideStep);
            }
            // 中间稀疏段
            double middleStart = sideLength;
            for (int i = 0; i < middlePoints; i++)
            {
                positions.Add(middleStart + i * middleStep);
            }
            // 右侧密集段（对称）
            double rightStart = totalLength - sideLength;
            for (int i = 0; i < oneSidePoints; i++)
            {
                positions.Add(rightStart + i * sideStep);
            }

            // 4. 修正最后一个点，确保它刚好等于辊幅宽
            if (positions.Count > 0)
            {
                positions[positions.Count - 1] = totalLength;
            }

            // 5. 计算每一步要移动的距离（相邻点的差值），×100 转成PLC用的单位
            for (int i = 1; i < positions.Count; i++)
            {
                double delta = positions[i] - positions[i - 1];
                moveSteps.Add((int)Math.Round(delta * 100));
            }

            // 日志输出，方便调试
            Log($"=== 变间距采集参数 ===");
            Log($"总点数：{caijidian} | 两侧点数：{sideTotalPoints}（各{oneSidePoints}点）");
            Log($"两侧步长：{sideStep:F2}mm | 中间步长：{middleStep:F2}mm");
            Log($"生成步数：{moveSteps.Count} 步");
            // 旋转角度不变
            int rotateAngle = 360 / muxian;
            // 3.基础音频文件夹路径
            string baseAudioFolder = @"D:\音频";
            Directory.CreateDirectory(baseAudioFolder); // 确保基础文件夹存在
                                                        // 防重复启动
                                                        // 防重复启动
            if (_collectThread != null && _collectThread.IsAlive)
            {
                if (_isStopping)
                    MessageBox.Show("采集正在结束中，请稍候再试");
                else
                    MessageBox.Show("正在采集中，请勿重复启动");
                return;
            }

            // 启动停止令牌
            _stopNow = false;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // 创建后台线程
            _collectThread = new Thread(() =>
            {
                bool stoppedByUser = false;
                try
                {
                    // 把所有参数传给后台方法
                    AutoCollectLogic(muxian, totalLength, moveSteps, caijidian, rotateAngle, baseAudioFolder, token);
                }
                catch (OperationCanceledException)
                {
                    stoppedByUser = true;
                }
                catch (Exception ex)
                {
                    // 后台线程里的UI操作必须用Invoke
                    this.Invoke((Action)(() =>
                    {
                        Log($"采集异常终止：{ex.Message}");
                    }));
                }
                finally
                {
                    Invoke(() =>
                    {
                        _collectThread = null;
                        _cts?.Dispose();
                        _cts = null;
                        _isStopping = false;
                        if (_stopNow) Log("✅ 已手动停止自动采集");
                        if (stoppedByUser)
                        {
                            MessageBox.Show("线程已停止"); // 现在一定会执行！
                            Log("✅ 采集已停止");
                        }
                        else
                        {
                            Log("✅ 采集完成");
                        }
                    });
                }
            });

            _collectThread.IsBackground = true;
            _collectThread.Start();
            // ==================== 【自适应版】按钮生成逻辑开始 ====================
            // 1. 先清空上次生成的所有按钮
            foreach (var btn in _dynamicButtons)
            {
                if (btn.Parent != null)
                    btn.Parent.Controls.Remove(btn);
                btn.Dispose();
            }
            _dynamicButtons.Clear();

            // 2. 获取 PictureBox 可用区域（留一点边距更美观）
            int containerWidth = pictureBox1.ClientSize.Width;
            int containerHeight = pictureBox1.ClientSize.Height;
            int padding = 8; // 整体内边距

            int usableWidth = containerWidth - 2 * padding;
            int usableHeight = containerHeight - 2 * padding;

            // 3. 自动计算：按钮大小 + 间距（自适应！）
            int rows = muxian;       // 行数 = 母线
            int cols = positions.Count;

            int btnWidth = usableWidth / cols;
            int btnHeight = usableHeight / rows;

            // 限制最大按钮大小，防止数量太少时按钮巨大
            btnWidth = Math.Min(btnWidth, 60);
            btnHeight = Math.Min(btnHeight, 60);

            // 计算总占用尺寸，用于居中
            int totalWidth = cols * btnWidth;
            int totalHeight = rows * btnHeight;

            // 居中偏移量
            int offsetX = (usableWidth - totalWidth) / 2 + padding;
            int offsetY = (usableHeight - totalHeight) / 2 + padding;

            // 4. 开始生成按钮
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    UIButton btn = new UIButton();
                    btn.Text = $"{row + 1}-{col + 1}";
                    btn.Width = btnWidth;
                    btn.Height = btnHeight;
                    btn.ForeColor = System.Drawing.Color.White;
                    btn.FillColor = System.Drawing.Color.Orange;      // 未采集 = 黄色/橙色
                    btn.FillHoverColor = System.Drawing.Color.Orange;
                    btn.FillPressColor = System.Drawing.Color.Orange;
                    btn.Radius = 4;
                    btn.Font = new System.Drawing.Font("Arial", 8);

                    // 自动定位
                    btn.Location = new Point(
                        offsetX + col * btnWidth,
                        offsetY + row * btnHeight
                    );

                    pictureBox1.Controls.Add(btn);
                    _dynamicButtons.Add(btn);
                }
            }
            SetTag(pictureBox1);
            // ==================== 【自适应版】按钮生成逻辑结束 ====================
            MessageBox.Show("自动采集已启动");
        }

        // ==================== 新增：带停止检查的延时方法 ====================
        private void SleepWithCancel(int milliseconds, CancellationToken token)
        {
            int totalSleep = 0;
            while (totalSleep < milliseconds)
            {
                if (_stopNow) return;
                token.ThrowIfCancellationRequested(); // 每50ms检查一次停止
                Thread.Sleep(50);
                totalSleep += 50;
            }
        }
        bool SHOP_BIAOZHI;
        // ==================== 新增：后台采集方法 ====================
        private void AutoCollectLogic(int muxian, int totalLength, List<int> moveSteps, int caijidian, int rotateAngle, string baseAudioFolder, CancellationToken token)
        {

            // ==================== 你原来的 整个 for 循环 完整搬过来 ====================
            string timeStr = DateTime.Now.ToString("yyyyMMddHHmmss");
            string timeFolder = Path.Combine(baseAudioFolder, timeStr);
            Directory.CreateDirectory(timeFolder);
            _lastCollectFolder = timeFolder;
            _lastMatrixRows = muxian;
            _lastMatrixCols = caijidian;
            for (int i = 0; i < muxian; i++)
            {
                // 检查停止信号（关键：能中途停止）
                token.ThrowIfCancellationRequested();
                if (_stopNow) return;
                // 创建母线文件夹
                string muxianFolder = Path.Combine(timeFolder, $"母线{i + 1}");
                Directory.CreateDirectory(muxianFolder);

                // ✅ 新增：日志显示开始采集第几条母线
                // ==============================================
                this.Invoke((Action)(() =>
                {
                    Log($"==============================================");
                    Log($"开始采集 第 {i + 1} 条母线");
                    Log($"==============================================");
                }));

                // 你原来的代码：方向
                bool isForward = (i % 2 == 0);
                int intValue = (i % 2 == 0) ? 0 : 1;
                //modbus.WriteInt32(1, 70, intValue);
                _modbusQueue.WriteInt32Async(1, 70, intValue).GetAwaiter().GetResult();

                for (int ii = 0; ii < caijidian; ii++)
                {
                    token.ThrowIfCancellationRequested();
                    if (_stopNow) return;

                    // ======================
                    // 1. 先敲击当前点 + 录音
                    // ======================
                    int fileIndex = isForward ? ii + 1 : caijidian - ii;
                    string fileName = $"{fileIndex}.wav";
                    string outputFilePath = Path.Combine(muxianFolder, fileName);

                    this.Invoke((Action)(() =>
                    {
                        this.outputFilePath = outputFilePath;
                        StartRecording(cbMicrophones.SelectedIndex);
                    }));

                    // 敲击
                    //modbus.WriteInt32(1, 65, 1);
                    _modbusQueue.WriteInt32Async(1, 65, 1).GetAwaiter().GetResult();
                    Thread.Sleep(50);
                    //modbus.WriteInt32(1, 65, 0);
                    _modbusQueue.WriteInt32Async(1, 65, 0).GetAwaiter().GetResult();
                    SleepWithCancel(300, token);

                    do
                    {
                        SHOP_BIAOZHI = !ceshitingzhi;

                    } while (SHOP_BIAOZHI);

                    this.Invoke((Action)(() =>
                    {
                        StopRecording();
                        Log($"第 {i + 1} 条母线 → 已完成第 {fileIndex} 个采集点敲击");
                    }));

                    // 👇👇👇【新增：把当前采集完成的按钮变绿】👇👇👇
                    this.Invoke((Action)(() =>
                    {
                        int buttonIndex = i * caijidian + (fileIndex - 1);
                        if (buttonIndex >= 0 && buttonIndex < _dynamicButtons.Count)
                        {
                            var btn = _dynamicButtons[buttonIndex];
                            btn.FillColor = System.Drawing.Color.Green;
                            btn.FillHoverColor = System.Drawing.Color.Green;
                            btn.FillPressColor = System.Drawing.Color.DarkGreen;
                        }
                    }));
                    // ======================
                    // 2. 不是最后一个点才移动
                    // ======================
                    //if (ii < caijidian - 1)
                    //{
                    //    // 移动步长
                    //    modbus.WriteInt32(1, 619, step);
                    if (ii < moveSteps.Count)
                    {
                        int currentStep = moveSteps[ii];
                        //modbus.WriteInt32(1, 619, currentStep);
                        _modbusQueue.WriteInt32Async(1, 619, currentStep).GetAwaiter().GetResult();
                        //modbus.WriteInt32(1, 67, 1);
                        _modbusQueue.WriteInt32Async(1, 67, 1).GetAwaiter().GetResult();
                        Thread.Sleep(50);
                        //modbus.WriteInt32(1, 67, 0);
                        _modbusQueue.WriteInt32Async(1, 67, 0).GetAwaiter().GetResult();

                        do
                        {
                            SHOP_BIAOZHI = !ceshitingzhi;

                        } while (SHOP_BIAOZHI);


                        // 等待到位
                        bool getposition;
                        do
                        {
                            token.ThrowIfCancellationRequested();
                            if (_stopNow) return;
                            //getposition = ReadInt32WithCancel(1, 25, token) == 1;
                            int posVal = _modbusQueue.ReadInt32Async(1, 25).GetAwaiter().GetResult();
                            getposition = posVal == 1;
                            do
                            {
                                SHOP_BIAOZHI = !ceshitingzhi;

                            } while (SHOP_BIAOZHI);
                            Thread.Sleep(50);
                        } while (!getposition);

                        do
                        {
                            SHOP_BIAOZHI = !ceshitingzhi;

                        } while (SHOP_BIAOZHI);
                        SleepWithCancel(500, token);
                    }

                    // ✅ 新增：日志显示敲击完成第几个点
                    // ==============================================
                    this.Invoke((Action)(() =>
                    {
                        Log($"第 {i + 1} 条母线 → 已完成第 {fileIndex} 个采集点敲击");
                    }));


                }

                // ✅ 新增：单条母线采集完成
                // ==============================================
                this.Invoke((Action)(() =>
                {
                    Log($"==================================================");
                    Log($"第 {i + 1} 条母线 全部采集点完成");
                    Log($"==================================================");
                }));

                // 旋转
                if (i < muxian - 1)
                {
                    token.ThrowIfCancellationRequested();

                    //modbus.WriteInt32(1, 621, rotateAngle);
                    _modbusQueue.WriteInt32Async(1, 621, rotateAngle).GetAwaiter().GetResult();
                    //modbus.WriteInt32(1, 68, 1);
                    _modbusQueue.WriteInt32Async(1, 68, 1).GetAwaiter().GetResult();
                    Thread.Sleep(50);
                    //modbus.WriteInt32(1, 68, 0);
                    _modbusQueue.WriteInt32Async(1, 68, 0).GetAwaiter().GetResult();
                    do
                    {
                        SHOP_BIAOZHI = !ceshitingzhi;

                    } while (SHOP_BIAOZHI);
                    bool rotateComplete;
                    do
                    {
                        token.ThrowIfCancellationRequested();
                        if (_stopNow) return;
                        //rotateComplete = ReadInt32WithCancel(1, 26, token) == 1;
                        int posVal1 = _modbusQueue.ReadInt32Async(1, 26).GetAwaiter().GetResult();
                        rotateComplete = posVal1 == 1;
                        do
                        {
                            SHOP_BIAOZHI = !ceshitingzhi;

                        } while (SHOP_BIAOZHI);
                        Thread.Sleep(50);
                    } while (!rotateComplete);

                    do
                    {
                        SHOP_BIAOZHI = !ceshitingzhi;

                    } while (SHOP_BIAOZHI);

                    SleepWithCancel(4000, token);

                    // 旋转完成日志
                    this.Invoke((Action)(() =>
                    {
                        Log($"已旋转至下一条母线，准备开始采集");
                    }));
                }
            }

            this.Invoke((Action)(() =>
            {
                Log("");
                Log("==============================================");
                Log("✅ 所有母线、所有采集点 全部采集完成！");
                Log("==============================================");
                Log("");
            }));
            //modbus.WriteInt32(1, 71, 1);
            _modbusQueue.WriteInt32Async(1, 71, 1).GetAwaiter().GetResult();
            Thread.Sleep(50);
            //modbus.WriteInt32(1, 71, 0);
            _modbusQueue.WriteInt32Async(1, 71, 0).GetAwaiter().GetResult();
            //modbus.WriteInt32(1, 73, 0);
            _modbusQueue.WriteInt32Async(1, 73, 0).GetAwaiter().GetResult();
            do
            {
                SHOP_BIAOZHI = !ceshitingzhi;

            } while (SHOP_BIAOZHI);
        }
        private void StartRecording(int deviceId)
        {
            try
            {
                waveIn = new WaveInEvent
                {
                    DeviceNumber = deviceId,
                    WaveFormat = new WaveFormat(44100, 16, 1), // 44.1kHz, 16位, 单声道
                    BufferMilliseconds = 500
                };

                writer = new WaveFileWriter(outputFilePath, waveIn.WaveFormat);

                waveIn.DataAvailable += (s, args) =>
                {
                    writer.Write(args.Buffer, 0, args.BytesRecorded);
                };

                waveIn.RecordingStopped += (s, args) =>
                {
                    writer?.Dispose();
                    waveIn?.Dispose();

                    this.Invoke((MethodInvoker)delegate
                    {
                        Log("录音完成!");
                        cbMicrophones.Enabled = true;
                    });
                };
                waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                this.Invoke((Action)(() =>
                {
                    Log($"录音出错: {ex.Message}");
                }));
            }
        }

        private void StopRecording()
        {
            if (waveIn != null)
            {
                waveIn.StopRecording();
            }
        }
        private void RefreshMicrophoneList()
        {
            microphones.Clear();
            cbMicrophones.Items.Clear();

            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                microphones.Add(capabilities);
                cbMicrophones.Items.Add(capabilities.ProductName);
            }

            if (cbMicrophones.Items.Count > 0)
            {
                cbMicrophones.SelectedIndex = 0;
            }
            else
            {
                Log("未找到可用的麦克风设备");
            }
        }

        private void Form1_Load(object sender, EventArgs e)

        {
            // ====== 日志框（uiListBox1）美化（适配Sunny.UI控件，无报错） ======
            uiListBox1.BackColor = System.Drawing.Color.FromArgb(33, 37, 43); // 深色背景，和输入框/面板统一
            uiListBox1.ForeColor = System.Drawing.Color.White; // 白色文字，高对比度，清晰可读
            uiListBox1.Font = new System.Drawing.Font("Consolas", 9.5f); // 等宽字体，日志排版更整齐

            // 开启滚动条（UIListBox自带属性，不用额外设置）
            // Sunny.UI的UIListBox会自动根据内容显示滚动条，无需手动开启

            // 可选：给日志框加一点金色外边框，和云图框呼应
            // 你可以用一个和日志框大小一样的Panel套住它，或者直接用下面的Paint事件
            // pictureBox2 必须留在右侧 panel2（设计器布局），不能放进日志区 panel1
            SetupPictureBox2HeatmapPanel();
            // ====== pictureBox1（云图显示区） ======
            panel_Cloud.BackColor = System.Drawing.Color.FromArgb(255, 191, 0); // 金色边框
            panel_Cloud.Padding = new System.Windows.Forms.Padding(2);
            pictureBox1.Parent = panel_Cloud;
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.BackColor = System.Drawing.Color.FromArgb(18, 22, 28);
            pictureBox1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;


            if (button1 != null)
            {
                button1.BackColor = System.Drawing.Color.DarkRed;
                button1.ForeColor = System.Drawing.Color.White;
                button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
                button1.FlatAppearance.BorderColor = System.Drawing.Color.White;
                button1.FlatAppearance.BorderSize = 1;
                button1.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Red;
                button1.FlatAppearance.MouseDownBackColor = System.Drawing.Color.Maroon;
            }
            var exportBtn = this.Controls.Find("uiButton_ExportReport", true).FirstOrDefault() as Sunny.UI.UIButton;
            if (exportBtn != null)
            {
                exportBtn.FillColor = System.Drawing.Color.Black;
                exportBtn.FillHoverColor = System.Drawing.Color.Gold;
                exportBtn.FillPressColor = System.Drawing.Color.DarkGoldenrod;
                exportBtn.ForeColor = System.Drawing.Color.White;
                exportBtn.RectColor = System.Drawing.Color.Transparent;
                exportBtn.Radius = 6;
            }
            // 背景图设置：拉伸填充整个窗体（不考虑变形）
            panelBackground.BackgroundImage = Properties.Resources.ScreenShot_2026_05_22_140957_714;
            panelBackground.BackgroundImageLayout = ImageLayout.Stretch; // 关键：把 Zoom 改成 Stretch
            panelBackground.Dock = DockStyle.Fill; // 关键：让 Panel 占满整个窗体
            panelBackground.SendToBack(); // 关键：把 Panel 放到所有控件后面，避免挡住按钮
            // 设置图片显示方式，推荐 Zoom（不变形）
            // 状态栏美化（和整体风格统一）
            uiLabel7.BackColor = System.Drawing.Color.FromArgb(180, 33, 37, 43); // 半透明深色背景
            uiLabel7.ForeColor = System.Drawing.Color.LightGreen; // 保持你原来的绿色文字

            uiLabel8.BackColor = System.Drawing.Color.FromArgb(180, 33, 37, 43);
            uiLabel8.ForeColor = System.Drawing.Color.Gold; // 手动状态改成金色，和按钮hover色呼应

            ApplyButtonStyleRecursive(this); // 递归美化所有按钮（包括GroupBox里）
            // 输入框美化（修复BorderColor错误，改用Sunny.UI的方式）
            foreach (System.Windows.Forms.Control c in this.Controls)
            {
                if (c is Sunny.UI.UITextBox txt)
                {
                    txt.BackColor = System.Drawing.Color.FromArgb(33, 37, 43);
                    txt.ForeColor = System.Drawing.Color.White;
                    txt.FillColor = System.Drawing.Color.FromArgb(33, 37, 43);
                    txt.RectColor = System.Drawing.Color.FromArgb(80, 86, 98); // 用RectColor代替BorderColor
                }
            }

            // 日志列表美化
            uiListBox1.BackColor = System.Drawing.Color.FromArgb(33, 37, 43);
            uiListBox1.ForeColor = System.Drawing.Color.White;

            // 云图显示区（修复PictureBox错误，用标准方式实现边框+圆角效果）
            pictureBox1.BackColor = System.Drawing.Color.FromArgb(18, 22, 28);
            pictureBox1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;

            // 状态标签美化
            uiLabel7.ForeColor = System.Drawing.Color.FromArgb(0, 230, 120);
            uiLabel8.ForeColor = System.Drawing.Color.FromArgb(255, 200, 0);
            // ==================== 面板/容器美化（必做！） ====================
            foreach (System.Windows.Forms.Control c in this.Controls)
            {
                if (c is System.Windows.Forms.Panel panel)
                {
                    // 深色半透明面板，和参考图的分区效果一致
                    panel.BackColor = System.Drawing.Color.FromArgb(35, 40, 50);
                    panel.ForeColor = System.Drawing.Color.White;
                    // 加金色边框，模拟全息面板效果
                    panel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
                }
                if (c is System.Windows.Forms.GroupBox groupBox)
                {
                    groupBox.BackColor = System.Drawing.Color.FromArgb(35, 40, 50);
                    groupBox.ForeColor = System.Drawing.Color.White;
                }
            }
            // 下拉框（麦克风选择）美化
            foreach (System.Windows.Forms.Control c in this.Controls)
            {
                if (c is Sunny.UI.UIComboBox comboBox)
                {
                    comboBox.BackColor = System.Drawing.Color.FromArgb(33, 37, 43);
                    comboBox.ForeColor = System.Drawing.Color.White;
                    comboBox.FillColor = System.Drawing.Color.FromArgb(33, 37, 43);
                    comboBox.RectColor = System.Drawing.Color.FromArgb(80, 86, 98);
                }
            }// 关键按钮特殊美化（终止按钮、导出报告）
            if (uiButton3 != null) // 你的终止按钮，名字根据你的设计器调整
            {
                uiButton3.FillColor = System.Drawing.Color.FromArgb(200, 0, 0); // 红色高亮
                uiButton3.FillHoverColor = System.Drawing.Color.FromArgb(255, 50, 50);
                uiButton3.FillPressColor = System.Drawing.Color.FromArgb(150, 0, 0);
                uiButton3.ForeColor = System.Drawing.Color.White;
            }

            // 云图框金色边框美化（仿全息效果）
            panel_Cloud.BackColor = System.Drawing.Color.FromArgb(255, 191, 0); // 金色边框
            panel_Cloud.Padding = new System.Windows.Forms.Padding(2); // 边框宽度
            pictureBox1.Parent = panel_Cloud;
            pictureBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            pictureBox1.BackColor = System.Drawing.Color.FromArgb(18, 22, 28);
            // 2. 让所有紫色按钮置顶，不被背景盖住，取消紫色按钮一直到btn。update
            结果显示.BringToFront();
            uButton15.BringToFront();
            uiButton16.BringToFront();
            uiButton_ExportReport.BringToFront();
            uiButton12.BringToFront();
            uiButton11.BringToFront();
            uiButton10.BringToFront();
            uiButton6.BringToFront();
            foreach (var btn in new[]
{
        uiButton13, uiButton6, uiButton10, uiButton12, uiButton11

    })
            {
                if (btn == null) continue;

                // 强制禁用主题，只认你设置的颜色
                btn.Style = Sunny.UI.UIStyle.Custom;
                btn.FillColor = System.Drawing.Color.FromArgb(64, 0, 64);
                btn.FillColor2 = System.Drawing.Color.FromArgb(64, 0, 64);
                btn.ForeColor = System.Drawing.Color.White;

                // 强制显示，不被覆盖
                btn.BringToFront();
                btn.Invalidate();
                btn.Update();
            }

            RefreshMicrophoneList();
            WireResultDisplayButton();
            WireManualPlcButtons();
        }

        #endregion

        private void ApplyButtonStyleRecursive(System.Windows.Forms.Control parent)
        {
            foreach (System.Windows.Forms.Control c in parent.Controls)
            {
                // 1. 普通按钮
                if (c is Sunny.UI.UIButton btn)
                {
                    btn.FillColorGradient = false;

                    if (btn.Name == "uiButton14") // 终止按钮
                    {
                        btn.FillColor = System.Drawing.Color.DarkRed;
                        btn.FillHoverColor = System.Drawing.Color.Red;
                        btn.FillPressColor = System.Drawing.Color.Maroon;
                        btn.ForeColor = System.Drawing.Color.White;
                        btn.RectColor = System.Drawing.Color.Transparent;
                        btn.Radius = 6;
                    }
                    else
                    {
                        btn.FillColor = System.Drawing.Color.Black;
                        btn.FillHoverColor = System.Drawing.Color.Gold;
                        btn.FillPressColor = System.Drawing.Color.DarkGoldenrod;
                        btn.ForeColor = System.Drawing.Color.White;
                        btn.RectColor = System.Drawing.Color.Transparent;
                        btn.Radius = 6;
                    }
                }

                // 2. 图标按钮
                else if (c is Sunny.UI.UISymbolButton symbolBtn)
                {
                    symbolBtn.Enabled = true;
                    symbolBtn.Style = Sunny.UI.UIStyle.Custom;
                    symbolBtn.FillColor = System.Drawing.Color.Black;
                    symbolBtn.FillHoverColor = System.Drawing.Color.Gold;
                    symbolBtn.FillPressColor = System.Drawing.Color.DarkGoldenrod;
                    symbolBtn.SymbolColor = System.Drawing.Color.White;
                    symbolBtn.RectColor = System.Drawing.Color.Transparent;
                    symbolBtn.Radius = 6;
                }

                // 3. 分组框
                else if (c is Sunny.UI.UIGroupBox group)
                {
                    group.FillColor = System.Drawing.Color.FromArgb(210, 15, 15, 20);
                    group.ForeColor = System.Drawing.Color.White;
                    group.RectColor = System.Drawing.Color.Transparent;
                    group.Radius = 8;
                }

                // 4. 普通 Panel
                else if (c is System.Windows.Forms.Panel panel)
                {
                    panel.BackColor = System.Drawing.Color.FromArgb(210, 15, 15, 20);
                    panel.ForeColor = System.Drawing.Color.White;
                }

                // 5. 文本框 / 下拉框
                else if (c is Sunny.UI.UITextBox txt)
                {
                    txt.FillColor = System.Drawing.Color.FromArgb(25, 25, 35);
                    txt.ForeColor = System.Drawing.Color.White;
                    txt.RectColor = System.Drawing.Color.Transparent;
                    txt.Radius = 6;
                }
                else if (c is Sunny.UI.UIComboBox cbo)
                {
                    cbo.FillColor = System.Drawing.Color.FromArgb(25, 25, 35);
                    cbo.ForeColor = System.Drawing.Color.White;
                    cbo.RectColor = System.Drawing.Color.Transparent;
                    cbo.Radius = 6;
                }

                // 6. UILabel（关键修改：和按钮风格统一）
                else if (c is Sunny.UI.UILabel label)
                {
                    // 背景色：和按钮一致的纯黑
                    label.BackColor = System.Drawing.Color.Black;
                    // 字体颜色：白色，和按钮文字保持一致
                    label.ForeColor = System.Drawing.Color.White;
                    // 去掉边框，让整体更丝滑
                    label.BorderStyle = System.Windows.Forms.BorderStyle.None;
                }

                // 递归遍历所有子控件
                if (c.HasChildren)
                    ApplyButtonStyleRecursive(c);
            }
        }

        #region 云图生成按钮
        public class AudioFeatures
        {
            public float DecayTime;
            public float SpectralCentroid;
            public float HighFreqRatio;
        }
        private async void uiButton8_Click(object sender, EventArgs e)
        {
            int rows = int.Parse(uiTextBox4.Text);
            int cols = int.Parse(uiTextBox5.Text);
            using (var fbd = new FolderBrowserDialog())
            {

                fbd.Description = $"请选择包含若干子文件夹的主文件夹（期望 行={rows}，列={cols}）";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    string rootFolder = fbd.SelectedPath;
                    string outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "分析结果");

                    // 确保输出目录存在
                    Directory.CreateDirectory(outputDir);

                    // 显示进度
                    Log("开始处理...");
                    try
                    {
                        // 异步执行避免UI冻结
                        float[,] matrix = await Task.Run(() =>
                        {
                            return ProcessFolderToMatrix(rootFolder, rows, cols);
                        });

                        Log("特征提取完成，正在生成可视化...");

                        await Task.Run(() =>
                        {
                            SaveAllMatrixPlots(matrix, outputDir);
                        });

                        // +++ 新增代码：在UI线程加载图像 +++
                        string interpImagePath = Path.Combine(outputDir, "matrix_cylinder_interp.png");
                        if (File.Exists(interpImagePath))
                        {
                            // 安全释放旧图像资源
                            if (pictureBox1.Image != null)
                            {
                                pictureBox1.Image.Dispose();
                            }

                            // 使用内存流避免文件锁定
                            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(interpImagePath)))
                            {
                                pictureBox1.Image = Image.FromStream(ms);
                            }
                        }
                        else
                        {
                            Log($"警告: 未找到图像文件 {interpImagePath}");
                        }
                        // --- 新增代码结束 ---

                        Log($"处理完成！结果已保存到: {outputDir}");
                        Log($"分析完成！4种可视化结果已保存到:\n{outputDir}");
                    }
                    catch (Exception ex)
                    {
                        Log($"批量处理出错: {ex.Message}，堆栈：{ex.StackTrace}");
                    }
                }
            }
        }

        public static float[,] ProcessFolderToMatrix(string rootFolder, int subFolderCount, int filesPerSubFolder)
        {
            float[,] matrix = new float[subFolderCount, filesPerSubFolder];
            var subFolders = Directory.GetDirectories(rootFolder);
            if (subFolders.Length < subFolderCount)
                throw new Exception($"子文件夹数量不足，实际为{subFolders.Length}");
            Array.Sort(subFolders); // 保证顺序
            for (int i = 0; i < subFolderCount; i++)
            {
                // 查找.wav和.mp3文件（不区分大小写）
                var allFiles = Directory.GetFiles(subFolders[i]);
                var audioFiles = allFiles.Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLower();
                    return ext == ".wav" || ext == ".mp3";
                }).ToArray();
                if (audioFiles.Length < filesPerSubFolder)
                    throw new Exception($"子文件夹{subFolders[i]}中的音频文件不足，实际为{audioFiles.Length}");
                Array.Sort(audioFiles); // 保证顺序
                for (int j = 0; j < filesPerSubFolder; j++)
                {
                    matrix[i, j] = ExtractHighFreqRatio(audioFiles[j]);
                }
            }
            return matrix;
        }

        public static float ExtractHighFreqRatio(string audioPath)
        {
            var features = DetectImpactSegment(audioPath);
            return features != null ? features.HighFreqRatio : 0f;
        }
        private const int SampleRate = 22050;
        private const int FrameLength = 512;
        private const double EnergyThresholdRatio = 0.3;
        static AudioFeatures DetectImpactSegment(string audioPath)
        {
            try
            {
                using (var reader = new AudioFileReader(audioPath))
                {
                    var resampler = new MediaFoundationResampler(reader, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1));
                    using (var resampled = resampler)
                    {
                        var sampleProvider = resampled.ToSampleProvider();
                        var buffer = new float[SampleRate * 10]; // 10秒缓冲区
                        int samplesRead = sampleProvider.Read(buffer, 0, buffer.Length);
                        int samples = samplesRead;

                        var energy = new System.Collections.Generic.List<double>();
                        int hopLength = FrameLength / 2;
                        for (int i = 0; i < samplesRead - FrameLength; i += hopLength)
                        {
                            double frameEnergy = 0;
                            for (int j = 0; j < FrameLength; j++)
                            {
                                frameEnergy += Math.Pow(buffer[i + j], 2);
                            }
                            energy.Add(frameEnergy);
                        }
                        if (energy.Count == 0)
                        {
                            MessageBox.Show($"文件 {Path.GetFileName(audioPath)} 能量计算失败");
                            return null;
                        }
                        double meanEnergy = energy.Average();
                        double stdEnergy = energy.StandardDeviation();
                        double threshold = meanEnergy + EnergyThresholdRatio * stdEnergy;
                        var impactFrames = energy.Select((e, idx) => new { Energy = e, Index = idx })
                                                .Where(x => x.Energy > threshold)
                                                .ToList();
                        if (impactFrames.Count > 0)
                        {
                            int startFrame = impactFrames[0].Index;
                            int startSample = startFrame * hopLength;
                            int endSample = startSample + FrameLength * 2;
                            endSample = Math.Min(endSample, samplesRead);
                            var impactSegment = new float[endSample - startSample];
                            Array.Copy(buffer, startSample, impactSegment, 0, impactSegment.Length);
                            return ExtractFeatures(impactSegment, SampleRate);
                        }
                        else
                        {
                            MessageBox.Show($"文件 {Path.GetFileName(audioPath)} 未检测到敲击片段");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"处理文件 {Path.GetFileName(audioPath)} 时出错: {ex.Message}");
            }
            return null;
        }

        static AudioFeatures ExtractFeatures(float[] segment, int sampleRate)
        {
            double[] preEmphasized = new double[segment.Length];
            for (int i = 1; i < segment.Length; i++)
            {
                preEmphasized[i] = segment[i] - 0.95 * segment[i - 1];
            }
            double maxDecay = preEmphasized.Max();
            double decayThreshold = 0.1 * maxDecay;
            int decayIndex = Array.FindIndex(preEmphasized, x => x < decayThreshold);
            float decayTime = decayIndex > 0 ? (float)decayIndex / sampleRate : 0;
            float spectralCentroid = CalculateSpectralCentroid(segment, sampleRate);
            float highFreqRatio = CalculateHighFreqRatio(segment, sampleRate);
            return new AudioFeatures
            {
                DecayTime = decayTime,
                SpectralCentroid = spectralCentroid,
                HighFreqRatio = highFreqRatio
            };
        }
        static float CalculateSpectralCentroid(float[] samples, int sampleRate)
        {
            int fftSize = 1;
            while (fftSize < samples.Length)
                fftSize *= 2;
            MathNet.Numerics.Complex32[] fftBuffer = new MathNet.Numerics.Complex32[fftSize];
            for (int i = 0; i < samples.Length; i++)
            {
                fftBuffer[i] = new MathNet.Numerics.Complex32(samples[i], 0);
            }
            for (int i = 0; i < fftSize; i++)
            {
                double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1)));
                fftBuffer[i] *= (float)window;
            }
            Fourier.Forward(fftBuffer, FourierOptions.NoScaling);
            float[] magnitudeSpectrum = new float[fftSize / 2];
            for (int i = 0; i < fftSize / 2; i++)
            {
                magnitudeSpectrum[i] = fftBuffer[i].Magnitude;
            }
            float numerator = 0;
            float denominator = 0;
            for (int i = 0; i < magnitudeSpectrum.Length; i++)
            {
                float frequency = i * (float)sampleRate / fftSize;
                numerator += frequency * magnitudeSpectrum[i];
                denominator += magnitudeSpectrum[i];
            }
            return denominator > 0 ? numerator / denominator : 0;
        }

        static float CalculateHighFreqRatio(float[] samples, int sampleRate)
        {
            int fftSize = 1;
            while (fftSize < samples.Length)
                fftSize *= 2;
            MathNet.Numerics.Complex32[] fftBuffer = new MathNet.Numerics.Complex32[fftSize];
            for (int i = 0; i < samples.Length; i++)
            {
                fftBuffer[i] = new MathNet.Numerics.Complex32(samples[i], 0);
            }
            for (int i = 0; i < fftSize; i++)
            {
                double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1)));
                fftBuffer[i] *= (float)window;
            }
            Fourier.Forward(fftBuffer, FourierOptions.NoScaling);
            float[] magnitudeSpectrum = new float[fftSize / 2];
            for (int i = 0; i < fftSize / 2; i++)
            {
                magnitudeSpectrum[i] = fftBuffer[i].Magnitude;
            }
            float freqResolution = (float)sampleRate / fftSize;
            int lowIdx = (int)(2000 / freqResolution);
            int highIdx = (int)(4000 / freqResolution);
            lowIdx = Math.Max(0, lowIdx);
            highIdx = Math.Min(magnitudeSpectrum.Length - 1, highIdx);
            float highFreqEnergy = 0;
            float totalEnergy = 0;
            for (int i = 0; i < magnitudeSpectrum.Length; i++)
            {
                float energy = magnitudeSpectrum[i] * magnitudeSpectrum[i];
                totalEnergy += energy;
                if (i >= lowIdx && i < highIdx)
                {
                    highFreqEnergy += energy;
                }
            }
            return totalEnergy > 0 ? highFreqEnergy / totalEnergy : 0;
        }

        public static void SaveAllMatrixPlots(float[,] matrix, string outputDir)
        {
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            SaveRawMatrixPlot(matrix, Path.Combine(outputDir, "matrix_raw.png"));
            SaveInterpMatrixPlot(matrix, Path.Combine(outputDir, "matrix_interp.png"));
            SaveCylinderMatrixPlot(matrix, Path.Combine(outputDir, "matrix_cylinder.png"));
            SaveCylinderInterpMatrixPlot(matrix, Path.Combine(outputDir, "matrix_cylinder_interp.png"));
        }

        // 保存原始二维点阵图（点更小+比色卡）
        public static void SaveRawMatrixPlot(float[,] matrix, string savePath, int cellSize = 20, int margin = 20, int pointSize = 4)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            int colorBarWidth = 18, colorBarMargin = 10, colorBarHeight = rows * cellSize;
            int width = cols * cellSize + 2 * margin + colorBarWidth + colorBarMargin + 40;
            int height = Math.Max(rows * cellSize + 2 * margin, colorBarHeight + margin);
            float maxVal = matrix.Cast<float>().Max();
            if (maxVal <= 0) maxVal = 1;
            using (var bmp = new System.Drawing.Bitmap(width, height))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            using (var font = new System.Drawing.Font("Arial", 10))
            {
                g.Clear(System.Drawing.Color.White);
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        float norm = matrix[i, j] / maxVal;
                        System.Drawing.Color color = GetColorByRatio(norm);
                        int x = margin + j * cellSize;
                        int y = margin + i * cellSize;
                        using (var brush = new System.Drawing.SolidBrush(color))
                        {
                            g.FillEllipse(brush, x - pointSize / 2, y - pointSize / 2, pointSize, pointSize);
                        }
                    }
                }
                // 比色卡
                int barX = margin + cols * cellSize + colorBarMargin;
                int barY = margin;
                DrawColorBar(g, barX, barY, colorBarWidth, colorBarHeight, 0, maxVal, "高频能量比", font);
                bmp.Save(savePath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // 保存插值二维点阵图（点更小+比色卡）
        public static void SaveInterpMatrixPlot(float[,] matrix, string savePath, int interpWidth = 400, int interpHeight = 400, int pointSize = 2)
        {
            int colorBarWidth = 18, colorBarMargin = 10, colorBarHeight = interpHeight;
            int width = interpWidth + colorBarWidth + colorBarMargin + 40;
            int height = interpHeight;
            float maxVal = matrix.Cast<float>().Max();
            if (maxVal <= 0) maxVal = 1;
            using (var bmp = new System.Drawing.Bitmap(width, height))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            using (var font = new System.Drawing.Font("Arial", 10))
            {
                g.Clear(System.Drawing.Color.White);
                for (int y = 0; y < interpHeight; y++)
                {
                    double srcI = (double)y / (interpHeight - 1) * (matrix.GetLength(0) - 1);
                    int i0 = (int)Math.Floor(srcI);
                    int i1 = Math.Min(i0 + 1, matrix.GetLength(0) - 1);
                    double tI = srcI - i0;
                    for (int x = 0; x < interpWidth; x++)
                    {
                        double srcJ = (double)x / (interpWidth - 1) * (matrix.GetLength(1) - 1);
                        int j0 = (int)Math.Floor(srcJ);
                        int j1 = Math.Min(j0 + 1, matrix.GetLength(1) - 1);
                        double tJ = srcJ - j0;
                        double v00 = matrix[i0, j0];
                        double v01 = matrix[i0, j1];
                        double v10 = matrix[i1, j0];
                        double v11 = matrix[i1, j1];
                        double v0 = v00 * (1 - tJ) + v01 * tJ;
                        double v1 = v10 * (1 - tJ) + v11 * tJ;
                        double v = v0 * (1 - tI) + v1 * tI;
                        double normV = v / maxVal;
                        System.Drawing.Color color = GetColorByRatio(normV);
                        using (var brush = new System.Drawing.SolidBrush(color))
                        {
                            g.FillEllipse(brush, x - pointSize / 2, y - pointSize / 2, pointSize, pointSize);
                        }
                    }
                }
                // 比色卡
                int barX = interpWidth + colorBarMargin;
                int barY = 0;
                DrawColorBar(g, barX, barY, colorBarWidth, colorBarHeight, 0, maxVal, "高频能量比", font);
                bmp.Save(savePath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // 保存三维圆柱点图（点自适应缩放、居中、点更小+比色卡）
        public static void SaveCylinderMatrixPlot(float[,] matrix, string savePath, int width = 400, int height = 400, double R = 150, double H = 300, int pointSize = 4)
        {
            int colorBarWidth = 18, colorBarMargin = 10, colorBarHeight = height - 40;
            int plotWidth = width;
            int plotHeight = height;
            int totalWidth = plotWidth + colorBarWidth + colorBarMargin + 40;
            int totalHeight = plotHeight;
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            float maxVal = matrix.Cast<float>().Max();
            if (maxVal <= 0) maxVal = 1;
            double angleY = 30, angleX = 20, viewerDistance = 800;
            // 计算三维坐标
            var coords = new (double x, double y, double z)[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                double theta = i * 2 * Math.PI / rows;
                for (int j = 0; j < cols; j++)
                {
                    double z = j * H / (cols - 1);
                    double x = R * Math.Cos(theta);
                    double y = R * Math.Sin(theta);
                    coords[i, j] = (x, y, z);
                }
            }
            // 投影所有点，找边界
            var pts = new List<System.Drawing.PointF>();
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    pts.Add(Project3DTo2D(coords[i, j].x, coords[i, j].y, coords[i, j].z, angleY, angleX, viewerDistance, 1, 1));
            float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            float minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            float scale = Math.Min((plotWidth - 40) / (maxX - minX), (plotHeight - 40) / (maxY - minY));
            float offsetX = (plotWidth - scale * (maxX + minX)) / 2;
            float offsetY = (plotHeight - scale * (maxY + minY)) / 2;
            using (var bmp = new System.Drawing.Bitmap(totalWidth, totalHeight))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            using (var font = new System.Drawing.Font("Arial", 10))
            {
                g.Clear(System.Drawing.Color.White);
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        double normRatio = matrix[i, j] / maxVal;
                        System.Drawing.Color color = GetColorByRatio(normRatio);
                        var (x, y, z) = coords[i, j];
                        var pt = Project3DTo2D(x, y, z, angleY, angleX, viewerDistance, 1, 1);
                        float px = pt.X * scale + offsetX;
                        float py = pt.Y * scale + offsetY;
                        using (var brush = new System.Drawing.SolidBrush(color))
                        {
                            g.FillEllipse(brush, px - pointSize / 2, py - pointSize / 2, pointSize, pointSize);
                        }
                    }
                }
                // 比色卡
                int barX = plotWidth + colorBarMargin;
                int barY = 20;
                DrawColorBar(g, barX, barY, colorBarWidth, colorBarHeight, 0, maxVal, "高频能量比", font);
                bmp.Save(savePath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // 保存三维圆柱插值点图（点自适应缩放、居中、点更小+比色卡）
        public static void SaveCylinderInterpMatrixPlot(float[,] matrix, string savePath, int width = 400, int height = 400, int interpRows = 200, int interpCols = 200, double R = 150, double H = 300, int pointSize = 2)
        {
            int colorBarWidth = 18, colorBarMargin = 10, colorBarHeight = height - 40;
            int plotWidth = width;
            int plotHeight = height;
            int totalWidth = plotWidth + colorBarWidth + colorBarMargin + 40;
            int totalHeight = plotHeight;
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            float maxVal = matrix.Cast<float>().Max();
            if (maxVal <= 0) maxVal = 1;
            double angleY = 30, angleX = 20, viewerDistance = 800;
            // 先计算所有插值点的投影坐标，找边界
            var pts = new List<System.Drawing.PointF>();
            for (int j = 0; j < interpCols; j++)
            {
                double fj = (double)j / (interpCols - 1) * (cols - 1);
                int j0 = (int)Math.Floor(fj);
                int j1 = Math.Min(j0 + 1, cols - 1);
                double tJ = fj - j0;
                for (int ii = 0; ii < interpRows; ii++)
                {
                    double fi = (double)ii / interpRows * rows;
                    int i0 = (int)Math.Floor(fi);
                    int i1 = Math.Min(i0 + 1, rows - 1);
                    double tI = fi - i0;
                    // 双线性插值
                    double v00 = matrix[i0, j0];
                    double v01 = matrix[i0, j1];
                    double v10 = matrix[i1, j0];
                    double v11 = matrix[i1, j1];
                    double v0 = v00 * (1 - tJ) + v01 * tJ;
                    double v1 = v10 * (1 - tJ) + v11 * tJ;
                    double v = v0 * (1 - tI) + v1 * tI;
                    // 计算三维坐标
                    double theta = fi * 2 * Math.PI / rows;
                    double z = fj * H / (cols - 1);
                    double x = R * Math.Cos(theta);
                    double y = R * Math.Sin(theta);
                    var pt = Project3DTo2D(x, y, z, angleY, angleX, viewerDistance, 1, 1);
                    pts.Add(pt);
                }
            }
            float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            float minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            float scale = Math.Min((plotWidth - 40) / (maxX - minX), (plotHeight - 40) / (maxY - minY));
            float offsetX = (plotWidth - scale * (maxX + minX)) / 2;
            float offsetY = (plotHeight - scale * (maxY + minY)) / 2;
            using (var bmp = new System.Drawing.Bitmap(totalWidth, totalHeight))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            using (var font = new System.Drawing.Font("Arial", 10))
            {
                g.Clear(System.Drawing.Color.White);
                int idx = 0;
                for (int j = 0; j < interpCols; j++)
                {
                    double fj = (double)j / (interpCols - 1) * (cols - 1);
                    int j0 = (int)Math.Floor(fj);
                    int j1 = Math.Min(j0 + 1, cols - 1);
                    double tJ = fj - j0;
                    for (int ii = 0; ii < interpRows; ii++)
                    {
                        double fi = (double)ii / interpRows * rows;
                        int i0 = (int)Math.Floor(fi);
                        int i1 = Math.Min(i0 + 1, rows - 1);
                        double tI = fi - i0;
                        // 双线性插值
                        double v00 = matrix[i0, j0];
                        double v01 = matrix[i0, j1];
                        double v10 = matrix[i1, j0];
                        double v11 = matrix[i1, j1];
                        double v0 = v00 * (1 - tJ) + v01 * tJ;
                        double v1 = v10 * (1 - tJ) + v11 * tJ;
                        double v = v0 * (1 - tI) + v1 * tI;
                        double normV = v / maxVal;
                        // 计算三维坐标
                        double theta = fi * 2 * Math.PI / rows;
                        double z = fj * H / (cols - 1);
                        double x = R * Math.Cos(theta);
                        double y = R * Math.Sin(theta);
                        var pt = Project3DTo2D(x, y, z, angleY, angleX, viewerDistance, 1, 1);
                        float px = pt.X * scale + offsetX;
                        float py = pt.Y * scale + offsetY;
                        System.Drawing.Color color = GetColorByRatio(normV);
                        using (var brush = new System.Drawing.SolidBrush(color))
                        {
                            g.FillEllipse(brush, px - pointSize / 2, py - pointSize / 2, pointSize, pointSize);
                        }
                        idx++;
                    }
                }
                // 比色卡
                int barX = plotWidth + colorBarMargin;
                int barY = 20;
                DrawColorBar(g, barX, barY, colorBarWidth, colorBarHeight, 0, maxVal, "高频能量比", font);
                bmp.Save(savePath, System.Drawing.Imaging.ImageFormat.Png);

            }
        }
        public static System.Drawing.PointF Project3DTo2D(double x, double y, double z, double angleY, double angleX, double viewerDistance, int width, int height)
        {
            double radY = angleY * Math.PI / 180.0;
            double radX = angleX * Math.PI / 180.0;
            double x1 = x * Math.Cos(radY) + z * Math.Sin(radY);
            double z1 = -x * Math.Sin(radY) + z * Math.Cos(radY);
            double y1 = y * Math.Cos(radX) - z1 * Math.Sin(radX);
            double z2 = y * Math.Sin(radX) + z1 * Math.Cos(radX);
            double factor = viewerDistance / (viewerDistance - z2);
            float px = (float)(width / 2 + x1 * factor);
            float py = (float)(height / 2 - y1 * factor);
            return new System.Drawing.PointF(px, py);
        }
        public static System.Drawing.Color GetColorByRatio(double ratio)
        {
            if (ratio <= 0) return System.Drawing.Color.Blue;
            if (ratio < 0.25)
                return System.Drawing.Color.FromArgb(0, (int)(ratio / 0.25 * 255), 255); // 蓝->青
            if (ratio < 0.5)
                return System.Drawing.Color.FromArgb(0, 255, 255 - (int)((ratio - 0.25) / 0.25 * 255)); // 青->绿
            if (ratio < 0.75)
                return System.Drawing.Color.FromArgb((int)((ratio - 0.5) / 0.25 * 255), 255, 0); // 绿->黄
            if (ratio < 1)
                return System.Drawing.Color.FromArgb(255, 255 - (int)((ratio - 0.75) / 0.25 * 255), 0); // 黄->红
            return System.Drawing.Color.Red;
        }
        public static void DrawColorBar(System.Drawing.Graphics g, int x, int y, int width, int height, float minVal, float maxVal, string label, System.Drawing.Font font)
        {
            for (int i = 0; i < height; i++)
            {
                double ratio = 1.0 - (double)i / (height - 1); // 上红下蓝
                System.Drawing.Color color = GetColorByRatio(ratio);
                using (var pen = new System.Drawing.Pen(color, width))
                {
                    g.DrawLine(pen, x, y + i, x + width - 1, y + i);
                }
            }
            // 边框
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.Black, 1))
            {
                g.DrawRectangle(pen, x, y, width - 1, height - 1);
            }
            // 数值说明
            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black))
            {
                g.DrawString($"{maxVal:F2}", font, brush, x + width + 2, y - 2);
                g.DrawString($"{minVal:F2}", font, brush, x + width + 2, y + height - font.Height);
                g.DrawString(label, font, brush, x - 10, y + height + 2);
            }
        }
        #endregion


        #endregion


        int FILEINDEX = 0;

        private void uiButton9_Click(object sender, EventArgs e)
        {

            // ==================== 1. 固定保存路径 ====================
            string baseFolder = @"D:\音频\手动采集";
            Directory.CreateDirectory(baseFolder); // 不存在自动创建

            // ==================== 2. 生成带时间的文件名 ====================
            string timeStr = DateTime.Now.ToString("yyyyMMdd_HHmmss"); // 时间戳
            string fileName = $"{timeStr}_{FILEINDEX}.wav";           // 时间+编号
            outputFilePath = Path.Combine(baseFolder, fileName);      // 拼接完整路径

            // ==================== 3. 开始录音 ====================
            StartRecording(cbMicrophones.SelectedIndex);

            // ==================== 4. 1.5秒后自动停止录音 ====================
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 1500;
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                timer.Dispose(); // 释放定时器
                StopRecording();
            };
            timer.Start();

            // ==================== 5. 触发敲击信号 ====================
            if (modbus != null && isConnected)
            {
                //modbus.WriteInt32(1, 65, 1);
                _modbusQueue.WriteInt32Async(1, 65, 1).GetAwaiter().GetResult();
                Thread.Sleep(100);
                //modbus.WriteInt32(1, 65, 0);
                _modbusQueue.WriteInt32Async(1, 65, 0).GetAwaiter().GetResult();
            }
            else
            {
                Log("手动采集：PLC未连接，仅录音");
            }

            // ==================== 6. 编号+1，日志提示 ====================
            Log($"手动录音完成：{outputFilePath}");
            FILEINDEX++;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            // 发送停止信号

            try
            {
                // 1. 没有运行的采集任务
                if (_cts == null || _collectThread == null || !_collectThread.IsAlive)
                {
                    Log("当前没有正在运行的采集任务");
                    return;
                }

                // 2. 已经在停止中，重复点击
                if (_isStopping)
                {
                    Log("采集正在结束中，请稍候再试");
                    return;
                }

                // 3. 正常触发停止
                _isStopping = true; // 标记为正在停止
                _stopNow = true;
                _cts.Cancel(); // 发送停止信号
                Log("自动采集正在停止...");
                Log("停止信号已发送，采集将在1秒内终止");
            }
            catch (Exception ex)
            {
                Log($"停止失败：{ex.Message}");
                _isStopping = false; // 异常时重置状态
            }
        }






        bool SHOUZHIDON = true;
        private async void uiButton4_Click(object sender, EventArgs e)
        {
            if (!EnsurePlcReady("手自动切换", silent: true)) return;
            try
            {
                if (SHOUZHIDON)
                {
                    SHOUZHIDON = !SHOUZHIDON;
                    uiLabel8.Text = "自动";
                    await WritePlcInt32Async(72, 0);
                }
                else
                {
                    SHOUZHIDON = !SHOUZHIDON;
                    uiLabel8.Text = "手动";
                    await WritePlcInt32Async(72, 1);
                }
            }
            catch
            {
                // 手自动切换失败时不写日志、不弹窗
            }
        }
        bool QIDONDTINGZHI = true;
        private async void uiButton5_Click(object sender, EventArgs e)
        {
            if (!EnsurePlcReady("启动/停止", silent: true)) return;
            try
            {
                if (QIDONDTINGZHI)
                {
                    QIDONDTINGZHI = !QIDONDTINGZHI;
                    uiButton5.Text = "未启动";
                    await WritePlcInt32Async(73, 0);
                }
                else
                {
                    QIDONDTINGZHI = !QIDONDTINGZHI;
                    uiButton5.Text = "已启动";
                    await WritePlcInt32Async(73, 1);
                }
            }
            catch
            {
                // 启动/停止失败时不写日志、不弹窗
            }
        }

        private void uiButton6_Click(object sender, EventArgs e)
        {

        }

        private void uiLabel4_Click(object sender, EventArgs e)
        {

        }

        private void uiButton12_Click(object sender, EventArgs e)
        {

        }

        private void uiLabel3_Click(object sender, EventArgs e)
        {

        }

        private void uiLabel9_Click(object sender, EventArgs e)
        {

        }

        private void monthCalendar1_DateChanged(object sender, DateRangeEventArgs e)
        {

        }
        bool ceshitingzhi = true;
        private void uiButton13_Click(object sender, EventArgs e)
        {
            if (ceshitingzhi)
            {
                ceshitingzhi = !ceshitingzhi;
                uiButton13.Text = "测试继续";
            }
            else
            {
                ceshitingzhi = !ceshitingzhi;
                uiButton13.Text = "测试停止";
            }
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox2_Click_1(object sender, EventArgs e)
        {

        }

        private void panel2_Paint(object sender, PaintEventArgs e)
        {

        }

        #region 结果显示热力图（仅 pictureBox2，不修改 PLC/界面设计）
        /// <summary>右侧 panel2 为聚类热力图区；勿将 pictureBox2 挂到日志区的 panel1。</summary>
        private void SetupPictureBox2HeatmapPanel()
        {
            if (panel2 == null || pictureBox2 == null) return;

            panel2.BackColor = System.Drawing.Color.FromArgb(255, 191, 0);
            panel2.Padding = new System.Windows.Forms.Padding(2);

            if (pictureBox2.Parent != panel2)
            {
                pictureBox2.Parent?.Controls.Remove(pictureBox2);
                panel2.Controls.Add(pictureBox2);
            }
            pictureBox2.Dock = DockStyle.Fill;
            pictureBox2.BackColor = System.Drawing.Color.FromArgb(18, 22, 28);
            pictureBox2.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox2.BringToFront();
            panel2.BringToFront();
            panel2.PerformLayout();
            pictureBox2.PerformLayout();
        }

        private static Image? LoadImageFile(string path)
        {
            if (!File.Exists(path)) return null;
            using Image loaded = Image.FromFile(path);
            return new Bitmap(loaded);
        }

        /// <summary>将热力图与点图上下拼接后显示在 pictureBox2。</summary>
        private static Image? CombineImagesVertically(Image top, Image bottom, int gap = 6)
        {
            int w = Math.Max(top.Width, bottom.Width);
            int h = top.Height + gap + bottom.Height;
            var combined = new Bitmap(w, h);
            using (var g = Graphics.FromImage(combined))
            {
                g.Clear(System.Drawing.Color.FromArgb(18, 22, 28));
                g.DrawImage(top, (w - top.Width) / 2, 0, top.Width, top.Height);
                g.DrawImage(bottom, (w - bottom.Width) / 2, top.Height + gap, bottom.Width, bottom.Height);
            }
            return combined;
        }

        private void ShowResultImagesOnPictureBox2(string heatmapPath, string pointplotPath)
        {
            void ApplyImage()
            {
                SetupPictureBox2HeatmapPanel();
                Image? heat = LoadImageFile(heatmapPath);
                Image? plot = LoadImageFile(pointplotPath);
                Image? display = null;
                if (heat != null && plot != null)
                    display = CombineImagesVertically(heat, plot);
                else if (heat != null)
                    display = heat;
                else if (plot != null)
                    display = plot;

                if (display == null)
                {
                    heat?.Dispose();
                    plot?.Dispose();
                    Log("未找到可显示的热力图或点图文件");
                    return;
                }

                if (heat != null && !ReferenceEquals(display, heat)) heat.Dispose();
                if (plot != null && !ReferenceEquals(display, plot)) plot.Dispose();

                Image? old = pictureBox2.Image;
                pictureBox2.Image = null;
                old?.Dispose();
                pictureBox2.Image = display;
                pictureBox2.Invalidate();
                pictureBox2.Update();
                panel2?.Invalidate();
            }

            if (InvokeRequired)
                Invoke(new Action(ApplyImage));
            else
                ApplyImage();
        }

        private async void 结果显示_Click(object sender, EventArgs e)
        {
            try
            {
                if (!int.TryParse(uiTextBox4.Text, out int rows) || rows <= 0)
                {
                    MessageBox.Show("请输入有效的母线数量（大于0）");
                    return;
                }
                if (!int.TryParse(uiTextBox5.Text, out int cols) || cols <= 0)
                {
                    MessageBox.Show("请输入有效的采集点数（大于0）");
                    return;
                }

                string? rootFolder = _lastCollectFolder;
                if (!string.IsNullOrEmpty(rootFolder) && Directory.Exists(rootFolder))
                {
                    if (_lastMatrixRows > 0) rows = _lastMatrixRows;
                    if (_lastMatrixCols > 0) cols = _lastMatrixCols;
                    Log($"使用本次采集目录: {rootFolder}");
                }
                else
                {
                    using (var fbd = new FolderBrowserDialog())
                    {
                        fbd.Description = $"请选择采集根目录（含「母线1」等子文件夹，行={rows}，列={cols}）";
                        if (fbd.ShowDialog() != DialogResult.OK)
                            return;
                        rootFolder = fbd.SelectedPath;
                    }
                }

                string outputDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "分析结果");
                Directory.CreateDirectory(outputDir);
                string heatmapPath = Path.Combine(outputDir, "cluster_heatmap_interp.png");
                string pointplotPath = Path.Combine(outputDir, "cluster_pointplot.png");

                try
                {
                    Log("正在生成热力图与点图…");
                    bool heatOk = false;
                    bool plotOk = false;
                    await Task.Run(() =>
                    {
                        heatOk = PycharmBridge.TryGenerateHeatmap(rootFolder!, rows, cols, heatmapPath, Log);
                        plotOk = PycharmBridge.TryGeneratePointplot(rootFolder!, rows, cols, pointplotPath, Log);
                    });
                    if (!heatOk && !plotOk)
                    {
                        Log("热力图与点图均生成失败，请检查 Python 环境与采集目录");
                        return;
                    }
                    if (!heatOk)
                        Log("热力图生成失败，仅显示点图");
                    if (!plotOk)
                        Log("点图生成失败，仅显示热力图");

                    ShowResultImagesOnPictureBox2(heatmapPath, pointplotPath);
                    Log("分析图已显示（热力图 + 点图）");
                }
                catch (Exception ex)
                {
                    Log($"结果显示处理出错: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log($"结果显示异常: {ex.Message}");
            }
            // 先判断：是否已经导入/采集了数据
            if (_currentHistoryData == null || _currentHistoryData.CollectData.Count == 0)
            {
                MessageBox.Show("请先导入历史数据或完成数据采集！");
                return;
            }

            // 示例逻辑：简单阈值判定（根据你的业务规则自行修改阈值）
            double avg = _currentHistoryData.CollectData.Average();
            double max = _currentHistoryData.CollectData.Max();
            double min = _currentHistoryData.CollectData.Min();

            string result;
            // 举例：自定义合格阈值
            if (avg >= 20 && avg <= 80)
                result = $"检测合格\r\n平均值：{avg:F2}\r\n最大值：{max:F2}\r\n最小值：{min:F2}";
            else
                result = $"检测异常\r\n平均值：{avg:F2}\r\n最大值：{max:F2}\r\n最小值：{min:F2}";

            // 展示结果
            MessageBox.Show(result, "检测结果");

            // 可选：同时把结果绘制到 pictureBox2 画布上
            RefreshResultPicture();
        }
        #endregion

        private void uButton15_Click_1(object sender, EventArgs e)
        {

            // 选择根文件夹（包含所有子文件夹）
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "请选择包含子文件夹及WAV文件的根目录";
                if (fbd.ShowDialog() != DialogResult.OK)
                    return;

                string rootFolder = fbd.SelectedPath;

                try
                {
                    // 获取所有一级子文件夹并排序
                    var subFolders = Directory.GetDirectories(rootFolder).OrderBy(d => d).ToList();
                    if (subFolders.Count == 0)
                    {
                        MessageBox.Show("所选目录下未找到子文件夹，请检查路径！");
                        return;
                    }

                    // 母线数量 = 子文件夹总数
                    int busLineCount = subFolders.Count;

                    // 取第一个子文件夹，统计WAV文件数量作为采集点数
                    var firstDirWavFiles = Directory.GetFiles(subFolders[0], "*.wav")
                                                    .OrderBy(f => f)
                                                    .ToList();
                    if (firstDirWavFiles.Count == 0)
                    {
                        MessageBox.Show($"子文件夹【{Path.GetFileName(subFolders[0])}】内未找到WAV文件！");
                        return;
                    }
                    int collectPointCount = firstDirWavFiles.Count;

                    // 回填界面文本框参数
                    uiTextBox4.Text = busLineCount.ToString();
                    uiTextBox5.Text = collectPointCount.ToString();

                    // 记录当前目录与矩阵尺寸，供给【结果分析/热力图】原有逻辑使用
                    _lastCollectFolder = rootFolder;
                    _lastMatrixRows = busLineCount;
                    _lastMatrixCols = collectPointCount;

                    Log($"已加载历史数据：母线数{busLineCount}，单路采集点数{collectPointCount}");

                    // 调用原有解析方法生成特征矩阵（和实时采集逻辑完全一致）
                    float[,] matrix = ProcessFolderToMatrix(rootFolder, busLineCount, collectPointCount);

                    // 调用你原有刷新绘图方法（和"结果分析"按钮共用一套绘图/热力图逻辑）
                    RefreshResultPicture();
                    RefreshCloudPicture();

                    MessageBox.Show("历史WAV数据导入并解析完成，可查看结果热力图！");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"数据解析失败：{ex.Message}");
                    Log($"历史数据导入异常：{ex.Message}");
                }
            }

        }
        public class InputForm : Form
        {
            private TextBox txtProductName;
            private TextBox txtSamplingLocation;
            private TextBox txtPackagingSpec;
            private DateTimePicker dtpProductionDate;
            private Button btnGenerate;
            private Button btnCancel;

            public InputForm()
            {
                SetupInputForm();
            }

            private void SetupInputForm()
            {
                this.Text = "输入报告信息";
                this.Size = new System.Drawing.Size(420, 300);
                this.StartPosition = FormStartPosition.CenterParent;
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox = false;
                this.MinimizeBox = false;

                Label lblName = new Label() { Text = "检品名称：", Location = new System.Drawing.Point(20, 20), AutoSize = true };
                txtProductName = new TextBox() { Location = new System.Drawing.Point(120, 17), Width = 240 };

                ///////////////////////////////////////////////////// ↓检品名称添加默认名修改在这↓ /////////////////////////////////////////////////////
                ///txtProductName.Text= "Name";
                ///////////////////////////////////////////////////// ↑检品名称添加默认名修改在这↑ /////////////////////////////////////////////////////


                Label lblLocation = new Label() { Text = "采样地点：", Location = new System.Drawing.Point(20, 60), AutoSize = true };
                txtSamplingLocation = new TextBox() { Location = new System.Drawing.Point(120, 57), Width = 240 };

                ///////////////////////////////////////////////////// ↓检品名称添加默认采样地点修改在这↓ /////////////////////////////////////////////////////
                ///txtSamplingLocation.Text= "location";
                ///////////////////////////////////////////////////// ↑检品名称添加默认采样地点修改在这↑ /////////////////////////////////////////////////////

                Label lblPackaging = new Label() { Text = "包装规格：", Location = new System.Drawing.Point(20, 100), AutoSize = true };
                txtPackagingSpec = new TextBox() { Location = new System.Drawing.Point(120, 97), Width = 240 };

                ///////////////////////////////////////////////////// ↓检品名称添加默认规格修改在这↓ /////////////////////////////////////////////////////
                ///txtPackagingSpec.Text= "直径2米，长度1.5米";
                ///////////////////////////////////////////////////// ↑检品名称添加默认规格修改在这↑ /////////////////////////////////////////////////////

                Label lblProdDate = new Label() { Text = "生产日期：", Location = new System.Drawing.Point(20, 140), AutoSize = true };
                dtpProductionDate = new DateTimePicker() { Location = new System.Drawing.Point(120, 137), Width = 240, Format = DateTimePickerFormat.Short };

                btnGenerate = new Button() { Text = "生成 Word 报告", Location = new System.Drawing.Point(100, 190), Size = new System.Drawing.Size(100, 35) };
                btnGenerate.Click += BtnGenerate_Click;

                btnCancel = new Button() { Text = "取消", Location = new System.Drawing.Point(220, 190), Size = new System.Drawing.Size(80, 35) };
                btnCancel.Click += (s, e) => this.Close();

                Controls.Add(lblName);
                Controls.Add(txtProductName);
                Controls.Add(lblLocation);
                Controls.Add(txtSamplingLocation);
                Controls.Add(lblPackaging);
                Controls.Add(txtPackagingSpec);
                Controls.Add(lblProdDate);
                Controls.Add(dtpProductionDate);
                Controls.Add(btnGenerate);
                Controls.Add(btnCancel);
            }

            private void BtnGenerate_Click(object sender, EventArgs e)
            {
                string productName = txtProductName.Text.Trim();
                string samplingLocation = txtSamplingLocation.Text.Trim();
                string packagingSpec = txtPackagingSpec.Text.Trim();
                DateTime productionDate = dtpProductionDate.Value;

                if (string.IsNullOrEmpty(productName) || string.IsNullOrEmpty(samplingLocation) || string.IsNullOrEmpty(packagingSpec))
                {
                    MessageBox.Show("请填写检品名称、采样地点和包装规格！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 使用 SaveFileDialog 让用户选择保存位置和文件名
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Title = "保存 Word 报告";
                saveFileDialog.Filter = "Word 文档 (*.docx)|*.docx";
                saveFileDialog.DefaultExt = "docx";

                ///////////////////////////////////////////////////// ↓保存文件默认名修改在这↓ /////////////////////////////////////////////////////
                saveFileDialog.FileName = $"阴极辊检测报告_{DateTime.Now:yyyyMMddHHmmss}.docx"; // 默认文件名
                                                                                         ///////////////////////////////////////////////////// ↑保存文件默认名修改在这↑ /////////////////////////////////////////////////////

                ///////////////////////////////////////////////////// ↓保存文件默认保存地址修改在这↓ /////////////////////////////////////////////////////
                saveFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); // 初始目录为桌面
                                                                                                                ///////////////////////////////////////////////////// ↑保存文件默认保存地址修改在这↑ /////////////////////////////////////////////////////

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = saveFileDialog.FileName;

                    try
                    {
                        GenerateWordReport(filePath, productName, samplingLocation, packagingSpec, productionDate);
                        MessageBox.Show($"报告已成功生成！\n保存路径：{filePath}", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
                        this.Close(); // 生成成功后关闭输入窗体
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"生成失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                // 如果用户取消选择，则不做任何操作
            }

            private void GenerateWordReport(string filePath, string productName, string samplingLocation, string packagingSpec, DateTime productionDate)
            {
                MessageBox.Show("正在使用代码自动生成Word，非模板复制");//新增
                using (WordprocessingDocument wordDoc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document))
                {
                    MainDocumentPart mainPart = wordDoc.AddMainDocumentPart();
                    mainPart.Document = new Document();
                    Body body = mainPart.Document.AppendChild(new Body());

                    Table table = new Table();

                    // 表格属性
                    TableProperties tblProp = new TableProperties();

                    // 边框
                    tblProp.AppendChild(new TableBorders(
                        new TopBorder() { Val = BorderValues.Single, Size = 12 },
                        new BottomBorder() { Val = BorderValues.Single, Size = 12 },
                        new LeftBorder() { Val = BorderValues.Single, Size = 12 },
                        new RightBorder() { Val = BorderValues.Single, Size = 12 },
                        new InsideHorizontalBorder() { Val = BorderValues.Single, Size = 12 },
                        new InsideVerticalBorder() { Val = BorderValues.Single, Size = 12 }
                    ));

                    // 表格宽度：页面宽度的 90%
                    tblProp.AppendChild(new TableWidth() { Width = "5000", Type = TableWidthUnitValues.Pct });

                    // 表格居中
                    tblProp.AppendChild(new TableJustification() { Val = TableRowAlignmentValues.Center });

                    // 单元格边距
                    TableCellMarginDefault cellMargin = new TableCellMarginDefault();
                    cellMargin.AppendChild(new TopMargin() { Width = "80", Type = TableWidthUnitValues.Dxa });
                    cellMargin.AppendChild(new BottomMargin() { Width = "80", Type = TableWidthUnitValues.Dxa });
                    cellMargin.AppendChild(new StartMargin() { Width = "120", Type = TableWidthUnitValues.Dxa });
                    cellMargin.AppendChild(new EndMargin() { Width = "120", Type = TableWidthUnitValues.Dxa });
                    tblProp.AppendChild(cellMargin);

                    table.AppendChild(tblProp);

                    // 辅助方法：创建标准单元格（始终包含 TableCellProperties）
                    TableCell CreateCell(string text, bool isLabel, JustificationValues alignment, int? columnSpan = null, int firstLineIndent = 0)
                    {
                        Paragraph para = new Paragraph();
                        ParagraphProperties paraProps = new ParagraphProperties();
                        if (alignment != JustificationValues.Left)
                            paraProps.AppendChild(new Justification() { Val = alignment });
                        if (firstLineIndent > 0)
                            paraProps.AppendChild(new Indentation() { FirstLine = firstLineIndent.ToString() });
                        para.AppendChild(paraProps);

                        Run run = new Run();
                        RunProperties runProps = new RunProperties();
                        runProps.AppendChild(new FontSize() { Val = "24" });
                        if (isLabel)
                            runProps.AppendChild(new Bold());
                        run.AppendChild(runProps);
                        run.AppendChild(new Text(text));
                        para.AppendChild(run);

                        TableCell cell = new TableCell();
                        cell.AppendChild(para);

                        // 始终添加 TableCellProperties，以便后续可添加 VerticalMerge 或 GridSpan
                        TableCellProperties cellProps = new TableCellProperties();
                        if (columnSpan.HasValue && columnSpan.Value > 1)
                            cellProps.AppendChild(new GridSpan() { Val = columnSpan.Value });
                        cell.AppendChild(cellProps);

                        return cell;
                    }

                    // 辅助方法：设置行高
                    void SetRowHeight(TableRow row, int heightInTwips)
                    {
                        TableRowProperties rowProps = new TableRowProperties();
                        rowProps.AppendChild(new TableRowHeight() { Val = (uint)heightInTwips, HeightType = new EnumValue<HeightRuleValues>(HeightRuleValues.AtLeast) });
                        row.AppendChild(rowProps);
                    }

                    int rowHeight = 600; // 每行最小高度（单位：twips）

                    // 行1：检品名称 / 生产日期
                    TableRow row1 = new TableRow();
                    row1.AppendChild(CreateCell("检品名称", true, JustificationValues.Center));
                    row1.AppendChild(CreateCell(productName, false, JustificationValues.Center));
                    row1.AppendChild(CreateCell("生产日期", true, JustificationValues.Center));
                    row1.AppendChild(CreateCell(productionDate.ToString("yyyy-MM-dd"), false, JustificationValues.Center));
                    SetRowHeight(row1, rowHeight);
                    table.AppendChild(row1);

                    // 行2：采样地点 / 包装规格
                    TableRow row2 = new TableRow();
                    row2.AppendChild(CreateCell("采样地点", true, JustificationValues.Center));
                    row2.AppendChild(CreateCell(samplingLocation, false, JustificationValues.Center));
                    row2.AppendChild(CreateCell("包装规格", true, JustificationValues.Center));
                    row2.AppendChild(CreateCell(packagingSpec, false, JustificationValues.Center));
                    SetRowHeight(row2, rowHeight);
                    table.AppendChild(row2);

                    // 行3：标准依据 / 检测日期
                    TableRow row3 = new TableRow();
                    row3.AppendChild(CreateCell("标准依据", true, JustificationValues.Center));
                    row3.AppendChild(CreateCell("", false, JustificationValues.Center));
                    row3.AppendChild(CreateCell("检测日期", true, JustificationValues.Center));

                    ///////////////////////////////////////////////////// ↓日期格式修改在这↓ /////////////////////////////////////////////////////
                    row3.AppendChild(CreateCell(DateTime.Now.ToString("yyyy-MM-dd"), false, JustificationValues.Center));
                    ///////////////////////////////////////////////////// ↑日期格式修改在这↑ /////////////////////////////////////////////////////

                    SetRowHeight(row3, rowHeight);
                    table.AppendChild(row3);

                    // 行4：检验方法（合并后3列）
                    TableRow row4 = new TableRow();
                    row4.AppendChild(CreateCell("检验方法", true, JustificationValues.Center));
                    row4.AppendChild(CreateCell("声发射识别法", false, JustificationValues.Center, 3));
                    SetRowHeight(row4, rowHeight);
                    table.AppendChild(row4);

                    // 行5：检测结果标题（合并全部4列）
                    TableRow row5 = new TableRow();
                    row5.AppendChild(CreateCell("检测结果", true, JustificationValues.Center, 4));
                    SetRowHeight(row5, rowHeight);
                    table.AppendChild(row5);

                    // ================= 合并区域：检测结果内容（9行纵向合并） =================
                    // 第1行（区域顶部）：带文本“非接触区占比10％”，并启动纵向合并
                    TableRow rowContent = new TableRow();

                    /////////////////////////////////////////////////////// ↓结论话语修改在这↓ /////////////////////////////////////////////////////
                    //TableCell contentCell = CreateCell("非接触区占比10％", false, JustificationValues.Left, 4, firstLineIndent: 240);
                    /////////////////////////////////////////////////////// ↑结论话语修改在这↑ /////////////////////////////////////////////////////
                    // ====================== 替换：单元格插入热力图片，不再填写文字 ======================
                    TableCell contentCell = new TableCell();
                    TableCellProperties contentCellProps = new TableCellProperties();
                    contentCellProps.AppendChild(new GridSpan() { Val = 4 });
                    // 设置纵向合并起始（保持原来9行合并逻辑不变）
                    contentCellProps.AppendChild(new VerticalMerge() { Val = new EnumValue<MergedCellValues>(MergedCellValues.Restart) });
                    contentCell.AppendChild(contentCellProps);

                    Paragraph paraImg = new Paragraph();
                    ParagraphProperties paraProp = new ParagraphProperties();
                    paraProp.AppendChild(new Justification() { Val = JustificationValues.Center }); //图片居中
                    paraImg.AppendChild(paraProp);

                    // 图片路径：和结果显示生成热力图完全统一
                    string imgPath = Path.Combine(
         Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
         "分析结果",
         "cluster_heatmap_interp.png"
     );
                    if (Application.OpenForms.OfType<Form1>().FirstOrDefault() is Form1 mainForm)
                    {
                        mainForm.Log($"图片检测路径：{imgPath}");
                        mainForm.Log($"文件是否存在：{File.Exists(imgPath)}");
                    }
                    Run runImg = new Run();
                    if (File.Exists(imgPath))
                    {
                        // 读取图片二进制 
                        byte[] imgBytes = File.ReadAllBytes(imgPath);
                        string picRelId = $"Pic_{Guid.NewGuid():N}";

                        //// 在文档主体添加图片部件
                        //DrawingsPart drawPart = mainPart.AddNewPart<DrawingsPart>(picRelId);
                        //ImagePart imgPart = drawPart.AddImagePart(ImagePartType.Png, mainPart.GetIdOfPart(drawPart));
                        //imgPart.FeedData(new MemoryStream(imgBytes));

                        //// 绘图尺寸：宽度4800000EMU(≈16cm)，自适应单元格，可按需修改
                        //DW.Inline inline = new DW.Inline(
                        //    new DW.Extent() { Cx = 4800000, Cy = 3600000 },
                        //    new DW.EffectExtent() { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                        //    new DW.DocProperties() { Id = 1, Name = "热力图.png" },
                        //    new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks() { NoChangeAspect = true }),
                        //    new A.Graphic(
                        //        new A.GraphicData(
                        //            new PIC.Picture(
                        //                new PIC.ShapeProperties(),
                        //                new PIC.BlipFill(
                        //                    new A.Blip() { Embed = picRelId },
                        //                    new A.Stretch(new A.FillRectangle())
                        //                )
                        //            )
                        //        )
                        //        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                        //    )
                        //);
                        //AltChunk altImg = new AltChunk { Id = picRelId };
                        //paraImg.AppendChild(altImg);
                        // ========== 替换开始 ==========
                        // 1. 正确创建图片部件（不再手动创建 DrawingsPart，避免层级错误）
                        ImagePart imgPart = mainPart.AddImagePart(ImagePartType.Png);
                        imgPart.FeedData(new MemoryStream(imgBytes));
                        // 获取图片真正的关系ID（关键：用这个ID给 Blip 引用）
                        string imgRelId = mainPart.GetIdOfPart(imgPart);

                        // 绘图尺寸保持你原有：4800000 / 3600000 EMU
                        DW.Inline inline = new DW.Inline(
                            new DW.Extent() { Cx = 4800000, Cy = 3600000 },
                            new DW.EffectExtent() { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                            new DW.DocProperties() { Id = 1, Name = "热力图.png" },
                            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks() { NoChangeAspect = true }),
                            new A.Graphic(
                                new A.GraphicData(
                                    new PIC.Picture(
                                        new PIC.ShapeProperties(),
                                        new PIC.BlipFill(
                                            // 这里必须用 imgRelId，不再用 picRelId
                                            new A.Blip() { Embed = imgRelId },
                                            new A.Stretch(new A.FillRectangle())
                                        )
                                    )
                                )
                                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                            )
                        );

                        // 把 Inline 放进 Drawing，再放进 Run（标准 Word 图文结构）
                        // 把 DW.Drawing 改成正确的类型，直接用完整命名空间或别名
                        DocumentFormat.OpenXml.Wordprocessing.Drawing drawing = new DocumentFormat.OpenXml.Wordprocessing.Drawing(inline);
                        runImg.AppendChild(drawing);
                        // ========== 替换结束 ==========
                    }
                    else
                    {
                        // 找不到图片时 fallback 文字
                        RunProperties rp = new RunProperties();
                        rp.AppendChild(new FontSize() { Val = "24" });
                        runImg.AppendChild(rp);
                        runImg.AppendChild(new Text("暂未生成热力图，请先采集数据后再生成报告"));
                    }
                    paraImg.AppendChild(runImg);
                    contentCell.AppendChild(paraImg);
                    // =================================================================================
                    // 获取刚创建的 TableCellProperties 并添加 VerticalMerge（起始）
                    contentCellProps = contentCell.GetFirstChild<TableCellProperties>()!;
                    contentCellProps.AppendChild(new VerticalMerge() { Val = new EnumValue<MergedCellValues>(MergedCellValues.Restart) });
                    rowContent.AppendChild(contentCell);
                    SetRowHeight(rowContent, rowHeight);
                    table.AppendChild(rowContent);

                    // 后续8行（空白行，纵向继续合并）
                    for (int i = 0; i < 8; i++)
                    {
                        TableRow emptyRow = new TableRow();
                        TableCell emptyCell = CreateCell("", false, JustificationValues.Left, 4);
                        // 获取刚创建的 TableCellProperties 并添加 VerticalMerge（继续）
                        TableCellProperties emptyCellProps = emptyCell.GetFirstChild<TableCellProperties>();
                        emptyCellProps.AppendChild(new VerticalMerge() { Val = new EnumValue<MergedCellValues>(MergedCellValues.Continue) });
                        emptyRow.AppendChild(emptyCell);
                        SetRowHeight(emptyRow, rowHeight);
                        table.AppendChild(emptyRow);
                    }
                    // ================= 合并区域结束 =================

                    // 检验结论（合并右侧3列）
                    TableRow row7 = new TableRow();
                    row7.AppendChild(CreateCell("检验结论", true, JustificationValues.Center));
                    row7.AppendChild(CreateCell("合格", false, JustificationValues.Center, 3));
                    SetRowHeight(row7, rowHeight);
                    table.AppendChild(row7);

                    // 检验人 / 审核人
                    TableRow row8 = new TableRow();
                    row8.AppendChild(CreateCell("检验人：", true, JustificationValues.Center));
                    row8.AppendChild(CreateCell("", false, JustificationValues.Center));
                    row8.AppendChild(CreateCell("审核人：", true, JustificationValues.Center));
                    row8.AppendChild(CreateCell("", false, JustificationValues.Center));
                    SetRowHeight(row8, rowHeight);
                    table.AppendChild(row8);

                    body.AppendChild(table);
                    body.AppendChild(new Paragraph());
                }
            }

            private void InitializeComponent()
            {
                SuspendLayout();
                // 
                // InputForm
                // 
                ClientSize = new Size(1147, 538);
                Name = "InputForm";
                Load += InputForm_Load;
                ResumeLayout(false);

            }

            private void InputForm_Load(object sender, EventArgs e)
            {

            }
        }
        /// <summary>仅用于「结果显示」调用 pycharm.py 生成热力图，与 PLC/界面无关。</summary>
        public static class FileHelper
        {
            /// <summary>
            /// 读取历史采集CSV/TXT文件
            /// </summary>
            /// <param name="filePath">文件路径</param>
            /// <returns>解析后的数据模型</returns>
            public static RollHistoryData ReadHistoryFile(string filePath)
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("文件不存在");

                var lines = File.ReadAllLines(filePath);
                var data = new RollHistoryData();
                data.CollectData = new List<double>();

                // 第一行：采集参数（和界面输入框一一对应）
                if (lines.Length >= 1)
                {
                    var param = lines[0].Split(',');
                    data.BusLineCount = int.TryParse(param[0], out int b) ? b : 0;
                    data.RollWidth = int.TryParse(param[1], out int w) ? w : 0;
                    data.CollectPointCount = int.TryParse(param[2], out int c) ? c : 0;
                    data.SideCollectLength = int.TryParse(param[3], out int l) ? l : 0;
                    data.SideCollectPoint = int.TryParse(param[4], out int p) ? p : 0;
                }

                // 第二行及以后：原始采集数值
                for (int i = 1; i < lines.Length; i++)
                {
                    var vals = lines[i].Split(',');
                    foreach (var v in vals)
                    {
                        if (double.TryParse(v, out double val))
                            data.CollectData.Add(val);
                    }
                }
                return data;
            }
        }
        public class RollHistoryData
        {
            // 采集参数（对应界面输入框）
            public int BusLineCount { get; set; }        // 母线数 uiTextBox4
            public int RollWidth { get; set; }           // 辊幅宽 uiTextBox6
            public int CollectPointCount { get; set; }   // 采集点数 uiTextBox5
            public int SideCollectLength { get; set; }  // 两侧采集长 uiTextBox10
            public int SideCollectPoint { get; set; }    // 两侧采集点 uiTextBox11

            // 原始采集波形/测点数据
            public List<double> CollectData { get; set; }
        }

        public static class PycharmBridge
        {
            public const string PyCharmScriptPath = @"D:\Pycharm2020\PycharmProject\pycharm.py";

            public sealed class RunResult
            {
                public bool Ok { get; set; }
                public string Stdout { get; set; } = "";
                public string Stderr { get; set; } = "";
                public int ExitCode { get; set; }
            }

            /// <summary>PyCharm 项目内解释器（若存在则优先于 PATH 中的 python）。</summary>
            public const string PyCharmPythonPath = @"D:\Pycharm2020\PycharmProject\venv\Scripts\python.exe";

            public static string ResolveScriptPath()
            {
                if (File.Exists(PyCharmScriptPath))
                    return PyCharmScriptPath;
                string[] fallbacks =
                {
                Path.Combine(Application.StartupPath, "pycharm.py"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pycharm.py"),
            };
                foreach (string p in fallbacks)
                {
                    if (File.Exists(p))
                        return p;
                }
                return PyCharmScriptPath;
            }

            public static string GetDefaultModelPath()
            {
                string script = ResolveScriptPath();
                string dir = Path.GetDirectoryName(script) ?? @"D:\Pycharm2020\PycharmProject";
                return Path.Combine(dir, "yinjigun_contact_model.joblib");
            }

            public static string? ResolvePythonExecutable()
            {
                if (File.Exists(PyCharmPythonPath) && TryPythonCandidate(PyCharmPythonPath, null) != null)
                    return PyCharmPythonPath;

                foreach (string name in new[] { "python", "python3", "py" })
                {
                    string? found = TryPythonCandidate(name, name.Equals("py", StringComparison.OrdinalIgnoreCase) ? "-3" : null);
                    if (found != null) return found;
                }

                string localPyRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Python");
                if (Directory.Exists(localPyRoot))
                {
                    foreach (string exe in Directory.EnumerateFiles(localPyRoot, "python.exe", SearchOption.AllDirectories))
                    {
                        if (TryPythonCandidate(exe, null) != null)
                            return exe;
                    }
                }
                return null;
            }

            private static string? TryPythonCandidate(string fileName, string? prefixArgs)
            {
                try
                {
                    string args = (prefixArgs != null ? prefixArgs + " " : "") + "-c \"import sys; sys.exit(0)\"";
                    var psi = new ProcessStartInfo(fileName, args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null) return null;
                    proc.WaitForExit(8000);
                    if (proc.ExitCode != 0) return null;
                    return fileName;
                }
                catch
                {
                    return null;
                }
            }

            public static RunResult RunCli(string argumentSuffix, int timeoutMs = 120000)
            {
                string? python = ResolvePythonExecutable();
                if (python == null)
                    return new RunResult { Ok = false, Stderr = "未找到 Python（请安装并加入 PATH）" };
                string script = ResolveScriptPath();
                if (!File.Exists(script))
                    return new RunResult { Ok = false, Stderr = $"未找到 pycharm.py: {script}" };

                var args = new StringBuilder();
                args.Append('"').Append(script).Append('"').Append(' ').Append(argumentSuffix);
                string processArgs = args.ToString();
                if (python.Equals("py", StringComparison.OrdinalIgnoreCase))
                    processArgs = "-3 " + processArgs;
                try
                {
                    var psi = new ProcessStartInfo(python, processArgs)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                        WorkingDirectory = Path.GetDirectoryName(script) ?? Application.StartupPath,
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null)
                        return new RunResult { Ok = false, Stderr = "无法启动 Python 进程" };
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(timeoutMs);
                    return new RunResult
                    {
                        Ok = proc.ExitCode == 0,
                        Stdout = stdout,
                        Stderr = stderr,
                        ExitCode = proc.ExitCode,
                    };
                }
                catch (Exception ex)
                {
                    return new RunResult { Ok = false, Stderr = ex.Message };
                }
            }

            public static bool TryGenerateHeatmap(string folder, int rows, int cols, string outputPng, Action<string>? log = null)
            {
                return TryRunImageCli("--heatmap-cli", folder, rows, cols, outputPng, log, "热力图");
            }

            public static bool TryGeneratePointplot(string folder, int rows, int cols, string outputPng, Action<string>? log = null)
            {
                return TryRunImageCli("--pointplot-cli", folder, rows, cols, outputPng, log, "点图");
            }

            private static bool TryRunImageCli(string cliFlag, string folder, int rows, int cols, string outputPng,
                Action<string>? log, string label)
            {
                var sb = new StringBuilder(cliFlag);
                sb.Append(" --folder \"").Append(folder).Append('"');
                sb.Append(" --rows ").Append(rows);
                sb.Append(" --cols ").Append(cols);
                sb.Append(" --output \"").Append(outputPng).Append('"');
                string model = GetDefaultModelPath();
                if (File.Exists(model))
                    sb.Append(" --model \"").Append(model).Append('"');
                var run = RunCli(sb.ToString(), 180000);
                if (!run.Ok)
                {
                    log?.Invoke($"{label}生成失败: {run.Stderr}\n{run.Stdout}");
                    return false;
                }
                return File.Exists(outputPng);
            }
        }


    }
}
