using Sunny.UI;
using S7.Net;
using NAudio.Wave;
using System.Windows.Forms;
using System.Linq;
using System.Collections.Generic;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.Statistics;
using System;
using System.IO;
using System.IO.Ports;
using NModbus.Device;
using Timer = System.Threading.Timer;
using System.Drawing;
using System.Threading;
using System.Threading;
using System.Threading.Tasks;

namespace 阴极辊控制与数据处理
{

    public partial class Form1 : UIForm
    {
        // ========== 新增：保存动态生成的按钮，方便清空
        private List<UIButton> _dynamicButtons = new List<UIButton>();

        // ========== 新增：后台线程+取消令牌（核心控制变量） ==========
        private Thread? _collectThread; // 自动采集后台线程
        private CancellationTokenSource? _cts; // 取消令牌（用于中途停止）
        private bool _isStopping = false; // 新增：是否正在停止中
        private bool _stopNow = false;
        public Form1()
        {
            InitializeComponent();
            this.AutoScaleMode = AutoScaleMode.None; // 等比放大

            // 3.基础音频文件夹路径
            string baseAudioFolder = @"D:\音频";
            Directory.CreateDirectory(baseAudioFolder); // 确保基础文件夹存在
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
                    uiLabel7.ForeColor = Color.Green;

                }
                catch (Exception ex)
                {
                    Log($"连接失败：{ex.Message}");
                }
            }
            else
            {
                connectionTimer?.Dispose();
                modbus?.Dispose();
                serialPort?.Close();
                isConnected = false;
                Log("连接失败，请检查");
            }
        }


        private void uiButton3_Click(object sender, EventArgs e)
        {
            if (!isConnected) return;

            // 关闭Modbus和串口
            modbus?.Dispose();
            if (serialPort != null && serialPort.IsOpen)
            {
                serialPort.Close();
            }
            serialPort?.Dispose();
            isConnected = false;
            Log("连接已断开");
            uiLabel7.Text = "已断开";
            uiLabel7.ForeColor = Color.Yellow;

        }


        private void uiButton2_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 66, 1);

                Log("发送 True 到 故障复位");
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiButton2_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isConnected) return;
            try
            {
                modbus.WriteInt32(1, 66, 0);
                Log("发送 False 到 故障复位");
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        #endregion


        #region 手动控制代码


        private void uiSymbolButton6_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 50, 1);
                // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiSymbolButton6_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 50, 0);  // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiSymbolButton4_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 51, 1);   // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiSymbolButton4_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 51, 0);   // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }


        private void uiSymbolButton2_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 60, 1);   // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiSymbolButton2_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 60, 0);   // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiSymbolButton1_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 61, 1);   // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiSymbolButton1_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 61, 0);   // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiSymbolButton5_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 55, 1);   // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiSymbolButton5_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 55, 0);   // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiSymbolButton3_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 56, 1);   // 线圈地址 0x10001
            }
            catch (Exception ex)
            {
                Log($"发送失败：{ex.Message}");
            }
        }

        private void uiSymbolButton3_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isConnected)
            {
                Log("请先连接PLC");
                return;
            }
            try
            {
                modbus.WriteInt32(1, 56, 0);   // 线圈地址 0x10001
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

            // 计算间隔（保留2位小数）
            double stepDouble = (double)totalLength / (caijidian - 1);
            stepDouble = Math.Round(stepDouble, 2);

            // ×100 取整（传给PLC用）
            int step = (int)Math.Round(stepDouble * 100);

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
                    AutoCollectLogic(muxian, totalLength, step, caijidian, rotateAngle, baseAudioFolder, token);
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
            int cols = caijidian;    // 列数 = 采集点

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
                    btn.ForeColor = Color.White;
                    btn.FillColor = Color.Gray;
                    btn.FillHoverColor = Color.Gray;
                    btn.FillPressColor = Color.DarkGray;
                    btn.Radius = 4;
                    btn.Font = new Font("Arial", 8);

                    // 自动定位
                    btn.Location = new Point(
                        offsetX + col * btnWidth,
                        offsetY + row * btnHeight
                    );

                    pictureBox1.Controls.Add(btn);
                    _dynamicButtons.Add(btn);
                }
            }
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

        // ==================== 新增：后台采集方法 ====================
        private void AutoCollectLogic(int muxian, int totalLength, int step, int caijidian, int rotateAngle, string baseAudioFolder, CancellationToken token)
        {

            // ==================== 你原来的 整个 for 循环 完整搬过来 ====================
            string timeStr = DateTime.Now.ToString("yyyyMMddHHmmss");
            string timeFolder = Path.Combine(baseAudioFolder, timeStr);
            Directory.CreateDirectory(timeFolder);
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
                modbus.WriteInt32(1, 70, intValue);

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
                    modbus.WriteInt32(1, 65, 1);
                    Thread.Sleep(50);
                    modbus.WriteInt32(1, 65, 0);
                    SleepWithCancel(300, token);


                    this.Invoke((Action)(() =>
                    {
                        StopRecording();
                        Log($"第 {i + 1} 条母线 → 已完成第 {fileIndex} 个采集点敲击");
                    }));

                    // ======================
                    // 2. 不是最后一个点才移动
                    // ======================
                    if (ii < caijidian - 1)
                    {
                        // 移动步长
                        modbus.WriteInt32(1, 619, step);
                        modbus.WriteInt32(1, 67, 1);
                        Thread.Sleep(50);
                        modbus.WriteInt32(1, 67, 0);

                        // 等待到位
                        bool getposition;
                        do
                        {
                            token.ThrowIfCancellationRequested();
                            if (_stopNow) return;
                            getposition = ReadInt32WithCancel(1, 25, token) == 1;
                            Thread.Sleep(50);
                        } while (!getposition);
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

                    modbus.WriteInt32(1, 621, rotateAngle);
                    modbus.WriteInt32(1, 68, 1);
                    Thread.Sleep(50);
                    modbus.WriteInt32(1, 68, 0);

                    bool rotateComplete;
                    do
                    {
                        token.ThrowIfCancellationRequested();
                        if (_stopNow) return;
                        rotateComplete = ReadInt32WithCancel(1, 26, token) == 1;
                        Thread.Sleep(50);
                    } while (!rotateComplete);
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
            modbus.WriteInt32(1, 71, 1);
            Thread.Sleep(50);
            modbus.WriteInt32(1, 71, 0);
            modbus.WriteInt32(1, 73, 0);

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
            RefreshMicrophoneList();
        }

        #endregion


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
                modbus.WriteInt32(1, 65, 1);
                Thread.Sleep(100);
                modbus.WriteInt32(1, 65, 0);
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



        private void uiSymbolButton4_Click(object sender, EventArgs e)
        {

        }

        private void uiSymbolButton6_Click(object sender, EventArgs e)
        {

        }

        private void uiLabel9_Click(object sender, EventArgs e)
        {

        }

        private void uiTextBox3_TextChanged(object sender, EventArgs e)
        {

        }

        private void uiTextBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void uiTextBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void uiLabel1_Click(object sender, EventArgs e)
        {

        }

        private void uiTextBox6_TextChanged(object sender, EventArgs e)
        {

        }

        private void uiGroupBox3_Click(object sender, EventArgs e)
        {

        }

        private void uiLabel8_Click(object sender, EventArgs e)
        {

        }
        bool SHOUZHIDON = true;
        private void uiButton4_Click(object sender, EventArgs e)
        {
            if (SHOUZHIDON)
            {
                SHOUZHIDON = !SHOUZHIDON;
                uiLabel8.Text = "自动";
                modbus.WriteInt32(1, 72, 0);

            }
            else
            {
                SHOUZHIDON = !SHOUZHIDON;
                uiLabel8.Text = "手动";
                modbus.WriteInt32(1, 72, 1);
            }
        }
        bool QIDONDTINGZHI = true;
        private void uiButton5_Click(object sender, EventArgs e)
        {

            if (QIDONDTINGZHI)
            {
                QIDONDTINGZHI = !QIDONDTINGZHI;
                uiButton5.Text = "未启动";
                modbus.WriteInt32(1, 73, 0);

            }
            else
            {
                QIDONDTINGZHI = !QIDONDTINGZHI;
                uiButton5.Text = "已启动";
                modbus.WriteInt32(1, 73, 1);
            }
        }

    }
}
