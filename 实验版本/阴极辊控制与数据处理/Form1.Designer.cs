using System.Windows.Forms;

namespace 阴极辊控制与数据处理
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            uiGroupBox1 = new Sunny.UI.UIGroupBox();
            uiGroupBox4 = new Sunny.UI.UIGroupBox();
            uiSymbolButton1 = new Sunny.UI.UISymbolButton();
            uiSymbolButton2 = new Sunny.UI.UISymbolButton();
            uiGroupBox5 = new Sunny.UI.UIGroupBox();
            uiSymbolButton3 = new Sunny.UI.UISymbolButton();
            uiSymbolButton5 = new Sunny.UI.UISymbolButton();
            uiGroupBox3 = new Sunny.UI.UIGroupBox();
            uiSymbolButton4 = new Sunny.UI.UISymbolButton();
            uiSymbolButton6 = new Sunny.UI.UISymbolButton();
            uiGroupBox2 = new Sunny.UI.UIGroupBox();
            uiTextBox6 = new Sunny.UI.UITextBox();
            uiLabel9 = new Sunny.UI.UILabel();
            cbMicrophones = new Sunny.UI.UIComboBox();
            uiButton8 = new Sunny.UI.UIButton();
            pictureBox1 = new PictureBox();
            uiButton7 = new Sunny.UI.UIButton();
            uiTextBox5 = new Sunny.UI.UITextBox();
            uiTextBox4 = new Sunny.UI.UITextBox();
            uiLabel5 = new Sunny.UI.UILabel();
            uiLabel3 = new Sunny.UI.UILabel();
            uiListBox1 = new Sunny.UI.UIListBox();
            uiButton1 = new Sunny.UI.UIButton();
            uiButton2 = new Sunny.UI.UIButton();
            uiButton3 = new Sunny.UI.UIButton();
            uiLabel6 = new Sunny.UI.UILabel();
            uiLabel7 = new Sunny.UI.UILabel();
            uiLabel8 = new Sunny.UI.UILabel();
            uiButton9 = new Sunny.UI.UIButton();
            button1 = new Button();
            uiButton4 = new Sunny.UI.UIButton();
            uiButton5 = new Sunny.UI.UIButton();
            uiGroupBox1.SuspendLayout();
            uiGroupBox4.SuspendLayout();
            uiGroupBox5.SuspendLayout();
            uiGroupBox3.SuspendLayout();
            uiGroupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // uiGroupBox1
            // 
            uiGroupBox1.Controls.Add(uiGroupBox4);
            uiGroupBox1.Controls.Add(uiGroupBox5);
            uiGroupBox1.Controls.Add(uiGroupBox3);
            uiGroupBox1.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiGroupBox1.Location = new Point(17, 194);
            uiGroupBox1.Margin = new Padding(4, 5, 4, 5);
            uiGroupBox1.MinimumSize = new Size(1, 1);
            uiGroupBox1.Name = "uiGroupBox1";
            uiGroupBox1.Padding = new Padding(0, 32, 0, 0);
            uiGroupBox1.Size = new Size(433, 503);
            uiGroupBox1.TabIndex = 0;
            uiGroupBox1.Text = "手动控制";
            uiGroupBox1.TextAlignment = ContentAlignment.MiddleLeft;
            // 
            // uiGroupBox4
            // 
            uiGroupBox4.Controls.Add(uiSymbolButton1);
            uiGroupBox4.Controls.Add(uiSymbolButton2);
            uiGroupBox4.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiGroupBox4.Location = new Point(8, 191);
            uiGroupBox4.Margin = new Padding(4, 5, 4, 5);
            uiGroupBox4.MinimumSize = new Size(1, 1);
            uiGroupBox4.Name = "uiGroupBox4";
            uiGroupBox4.Padding = new Padding(0, 32, 0, 0);
            uiGroupBox4.Size = new Size(412, 148);
            uiGroupBox4.TabIndex = 1;
            uiGroupBox4.Text = "纵向运动";
            uiGroupBox4.TextAlignment = ContentAlignment.MiddleLeft;
            // 
            // uiSymbolButton1
            // 
            uiSymbolButton1.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton1.Location = new Point(318, 35);
            uiSymbolButton1.MinimumSize = new Size(1, 1);
            uiSymbolButton1.Name = "uiSymbolButton1";
            uiSymbolButton1.Size = new Size(64, 100);
            uiSymbolButton1.Style = Sunny.UI.UIStyle.Custom;
            uiSymbolButton1.Symbol = 57418;
            uiSymbolButton1.SymbolColor = Color.Cyan;
            uiSymbolButton1.SymbolSize = 50;
            uiSymbolButton1.TabIndex = 13;
            uiSymbolButton1.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton1.MouseDown += uiSymbolButton1_MouseDown;
            uiSymbolButton1.MouseUp += uiSymbolButton1_MouseUp;
            // 
            // uiSymbolButton2
            // 
            uiSymbolButton2.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton2.Location = new Point(226, 35);
            uiSymbolButton2.MinimumSize = new Size(1, 1);
            uiSymbolButton2.Name = "uiSymbolButton2";
            uiSymbolButton2.Size = new Size(64, 100);
            uiSymbolButton2.Style = Sunny.UI.UIStyle.Custom;
            uiSymbolButton2.Symbol = 57417;
            uiSymbolButton2.SymbolColor = Color.Cyan;
            uiSymbolButton2.SymbolSize = 50;
            uiSymbolButton2.TabIndex = 12;
            uiSymbolButton2.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton2.MouseDown += uiSymbolButton2_MouseDown;
            uiSymbolButton2.MouseUp += uiSymbolButton2_MouseUp;
            // 
            // uiGroupBox5
            // 
            uiGroupBox5.Controls.Add(uiSymbolButton3);
            uiGroupBox5.Controls.Add(uiSymbolButton5);
            uiGroupBox5.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiGroupBox5.Location = new Point(8, 345);
            uiGroupBox5.Margin = new Padding(4, 5, 4, 5);
            uiGroupBox5.MinimumSize = new Size(1, 1);
            uiGroupBox5.Name = "uiGroupBox5";
            uiGroupBox5.Padding = new Padding(0, 32, 0, 0);
            uiGroupBox5.Size = new Size(412, 148);
            uiGroupBox5.TabIndex = 1;
            uiGroupBox5.Text = "回转运动";
            uiGroupBox5.TextAlignment = ContentAlignment.MiddleLeft;
            // 
            // uiSymbolButton3
            // 
            uiSymbolButton3.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton3.Location = new Point(318, 35);
            uiSymbolButton3.MinimumSize = new Size(1, 1);
            uiSymbolButton3.Name = "uiSymbolButton3";
            uiSymbolButton3.Size = new Size(64, 100);
            uiSymbolButton3.Style = Sunny.UI.UIStyle.Custom;
            uiSymbolButton3.Symbol = 57418;
            uiSymbolButton3.SymbolColor = Color.Cyan;
            uiSymbolButton3.SymbolSize = 50;
            uiSymbolButton3.TabIndex = 13;
            uiSymbolButton3.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton3.MouseDown += uiSymbolButton3_MouseDown;
            uiSymbolButton3.MouseUp += uiSymbolButton3_MouseUp;
            // 
            // uiSymbolButton5
            // 
            uiSymbolButton5.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton5.Location = new Point(226, 35);
            uiSymbolButton5.MinimumSize = new Size(1, 1);
            uiSymbolButton5.Name = "uiSymbolButton5";
            uiSymbolButton5.Size = new Size(64, 100);
            uiSymbolButton5.Style = Sunny.UI.UIStyle.Custom;
            uiSymbolButton5.Symbol = 57417;
            uiSymbolButton5.SymbolColor = Color.Cyan;
            uiSymbolButton5.SymbolSize = 50;
            uiSymbolButton5.TabIndex = 12;
            uiSymbolButton5.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton5.MouseDown += uiSymbolButton5_MouseDown;
            uiSymbolButton5.MouseUp += uiSymbolButton5_MouseUp;
            // 
            // uiGroupBox3
            // 
            uiGroupBox3.Controls.Add(uiSymbolButton4);
            uiGroupBox3.Controls.Add(uiSymbolButton6);
            uiGroupBox3.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiGroupBox3.Location = new Point(8, 37);
            uiGroupBox3.Margin = new Padding(4, 5, 4, 5);
            uiGroupBox3.MinimumSize = new Size(1, 1);
            uiGroupBox3.Name = "uiGroupBox3";
            uiGroupBox3.Padding = new Padding(0, 32, 0, 0);
            uiGroupBox3.Size = new Size(412, 148);
            uiGroupBox3.TabIndex = 0;
            uiGroupBox3.Text = "轴向运动";
            uiGroupBox3.TextAlignment = ContentAlignment.MiddleLeft;
            uiGroupBox3.Click += uiGroupBox3_Click;
            // 
            // uiSymbolButton4
            // 
            uiSymbolButton4.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton4.Location = new Point(226, 35);
            uiSymbolButton4.MinimumSize = new Size(1, 1);
            uiSymbolButton4.Name = "uiSymbolButton4";
            uiSymbolButton4.Size = new Size(64, 100);
            uiSymbolButton4.Style = Sunny.UI.UIStyle.Custom;
            uiSymbolButton4.Symbol = 57418;
            uiSymbolButton4.SymbolColor = Color.Cyan;
            uiSymbolButton4.SymbolRotate = 180;
            uiSymbolButton4.SymbolSize = 50;
            uiSymbolButton4.TabIndex = 8;
            uiSymbolButton4.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton4.Click += uiSymbolButton4_Click;
            uiSymbolButton4.MouseDown += uiSymbolButton4_MouseDown;
            uiSymbolButton4.MouseUp += uiSymbolButton4_MouseUp;
            // 
            // uiSymbolButton6
            // 
            uiSymbolButton6.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton6.Location = new Point(318, 35);
            uiSymbolButton6.MinimumSize = new Size(1, 1);
            uiSymbolButton6.Name = "uiSymbolButton6";
            uiSymbolButton6.Size = new Size(64, 100);
            uiSymbolButton6.Style = Sunny.UI.UIStyle.Custom;
            uiSymbolButton6.Symbol = 57417;
            uiSymbolButton6.SymbolColor = Color.Cyan;
            uiSymbolButton6.SymbolRotate = 180;
            uiSymbolButton6.SymbolSize = 50;
            uiSymbolButton6.TabIndex = 6;
            uiSymbolButton6.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiSymbolButton6.Click += uiSymbolButton6_Click;
            uiSymbolButton6.MouseDown += uiSymbolButton6_MouseDown;
            uiSymbolButton6.MouseUp += uiSymbolButton6_MouseUp;
            // 
            // uiGroupBox2
            // 
            uiGroupBox2.Controls.Add(uiTextBox6);
            uiGroupBox2.Controls.Add(uiLabel9);
            uiGroupBox2.Controls.Add(cbMicrophones);
            uiGroupBox2.Controls.Add(uiButton8);
            uiGroupBox2.Controls.Add(pictureBox1);
            uiGroupBox2.Controls.Add(uiButton7);
            uiGroupBox2.Controls.Add(uiTextBox5);
            uiGroupBox2.Controls.Add(uiTextBox4);
            uiGroupBox2.Controls.Add(uiLabel5);
            uiGroupBox2.Controls.Add(uiLabel3);
            uiGroupBox2.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiGroupBox2.Location = new Point(458, 194);
            uiGroupBox2.Margin = new Padding(4, 5, 4, 5);
            uiGroupBox2.MinimumSize = new Size(1, 1);
            uiGroupBox2.Name = "uiGroupBox2";
            uiGroupBox2.Padding = new Padding(0, 32, 0, 0);
            uiGroupBox2.Size = new Size(433, 503);
            uiGroupBox2.TabIndex = 1;
            uiGroupBox2.Text = "自动采集与数据分析";
            uiGroupBox2.TextAlignment = ContentAlignment.MiddleLeft;
            // 
            // uiTextBox6
            // 
            uiTextBox6.DoubleValue = 300D;
            uiTextBox6.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiTextBox6.IntValue = 300;
            uiTextBox6.Location = new Point(130, 53);
            uiTextBox6.Margin = new Padding(4, 5, 4, 5);
            uiTextBox6.MinimumSize = new Size(1, 16);
            uiTextBox6.Name = "uiTextBox6";
            uiTextBox6.Padding = new Padding(5);
            uiTextBox6.ShowText = false;
            uiTextBox6.Size = new Size(66, 29);
            uiTextBox6.TabIndex = 14;
            uiTextBox6.Text = "300";
            uiTextBox6.TextAlignment = ContentAlignment.MiddleLeft;
            uiTextBox6.Watermark = "";
            uiTextBox6.TextChanged += uiTextBox6_TextChanged;
            // 
            // uiLabel9
            // 
            uiLabel9.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiLabel9.ForeColor = Color.FromArgb(48, 48, 48);
            uiLabel9.Location = new Point(17, 59);
            uiLabel9.Name = "uiLabel9";
            uiLabel9.Size = new Size(104, 23);
            uiLabel9.TabIndex = 18;
            uiLabel9.Text = "辊幅宽：";
            uiLabel9.Click += uiLabel9_Click;
            // 
            // cbMicrophones
            // 
            cbMicrophones.DataSource = null;
            cbMicrophones.FillColor = Color.White;
            cbMicrophones.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            cbMicrophones.ItemHoverColor = Color.FromArgb(155, 200, 255);
            cbMicrophones.ItemSelectForeColor = Color.FromArgb(235, 243, 255);
            cbMicrophones.Location = new Point(19, 123);
            cbMicrophones.Margin = new Padding(4, 5, 4, 5);
            cbMicrophones.MinimumSize = new Size(63, 0);
            cbMicrophones.Name = "cbMicrophones";
            cbMicrophones.Padding = new Padding(0, 0, 30, 2);
            cbMicrophones.Size = new Size(177, 29);
            cbMicrophones.SymbolSize = 24;
            cbMicrophones.TabIndex = 17;
            cbMicrophones.Text = "可用麦克风";
            cbMicrophones.TextAlignment = ContentAlignment.MiddleLeft;
            cbMicrophones.Watermark = "";
            // 
            // uiButton8
            // 
            uiButton8.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton8.Location = new Point(318, 48);
            uiButton8.MinimumSize = new Size(1, 1);
            uiButton8.Name = "uiButton8";
            uiButton8.Size = new Size(79, 69);
            uiButton8.TabIndex = 16;
            uiButton8.Text = "云图生成";
            uiButton8.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton8.Click += uiButton8_Click;
            // 
            // pictureBox1
            // 
            pictureBox1.Location = new Point(29, 164);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(375, 316);
            pictureBox1.TabIndex = 15;
            pictureBox1.TabStop = false;
            // 
            // uiButton7
            // 
            uiButton7.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton7.Location = new Point(219, 48);
            uiButton7.MinimumSize = new Size(1, 1);
            uiButton7.Name = "uiButton7";
            uiButton7.Size = new Size(79, 69);
            uiButton7.TabIndex = 14;
            uiButton7.Text = "自动采集";
            uiButton7.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton7.Click += uiButton7_Click;
            // 
            // uiTextBox5
            // 
            uiTextBox5.DoubleValue = 100D;
            uiTextBox5.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiTextBox5.IntValue = 100;
            uiTextBox5.Location = new Point(130, 84);
            uiTextBox5.Margin = new Padding(4, 5, 4, 5);
            uiTextBox5.MinimumSize = new Size(1, 16);
            uiTextBox5.Name = "uiTextBox5";
            uiTextBox5.Padding = new Padding(5);
            uiTextBox5.ShowText = false;
            uiTextBox5.Size = new Size(66, 29);
            uiTextBox5.TabIndex = 13;
            uiTextBox5.Text = "100";
            uiTextBox5.TextAlignment = ContentAlignment.MiddleLeft;
            uiTextBox5.Watermark = "";
            // 
            // uiTextBox4
            // 
            uiTextBox4.DoubleValue = 10D;
            uiTextBox4.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiTextBox4.IntValue = 10;
            uiTextBox4.Location = new Point(130, 22);
            uiTextBox4.Margin = new Padding(4, 5, 4, 5);
            uiTextBox4.MinimumSize = new Size(1, 16);
            uiTextBox4.Name = "uiTextBox4";
            uiTextBox4.Padding = new Padding(5);
            uiTextBox4.ShowText = false;
            uiTextBox4.Size = new Size(66, 29);
            uiTextBox4.TabIndex = 12;
            uiTextBox4.Text = "10";
            uiTextBox4.TextAlignment = ContentAlignment.MiddleLeft;
            uiTextBox4.Watermark = "";
            // 
            // uiLabel5
            // 
            uiLabel5.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiLabel5.ForeColor = Color.FromArgb(48, 48, 48);
            uiLabel5.Location = new Point(17, 89);
            uiLabel5.Name = "uiLabel5";
            uiLabel5.Size = new Size(116, 23);
            uiLabel5.TabIndex = 11;
            uiLabel5.Text = "采集点数：";
            // 
            // uiLabel3
            // 
            uiLabel3.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiLabel3.ForeColor = Color.FromArgb(48, 48, 48);
            uiLabel3.Location = new Point(17, 32);
            uiLabel3.Name = "uiLabel3";
            uiLabel3.Size = new Size(98, 19);
            uiLabel3.TabIndex = 10;
            uiLabel3.Text = "母线数量：";
            // 
            // uiListBox1
            // 
            uiListBox1.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiListBox1.HoverColor = Color.FromArgb(155, 200, 255);
            uiListBox1.ItemSelectForeColor = Color.White;
            uiListBox1.Location = new Point(458, 57);
            uiListBox1.Margin = new Padding(4, 5, 4, 5);
            uiListBox1.MinimumSize = new Size(1, 1);
            uiListBox1.Name = "uiListBox1";
            uiListBox1.Padding = new Padding(2);
            uiListBox1.ShowText = false;
            uiListBox1.Size = new Size(433, 127);
            uiListBox1.TabIndex = 2;
            uiListBox1.Text = "uiListBox1";
            // 
            // uiButton1
            // 
            uiButton1.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton1.Location = new Point(25, 87);
            uiButton1.MinimumSize = new Size(1, 1);
            uiButton1.Name = "uiButton1";
            uiButton1.Size = new Size(100, 51);
            uiButton1.TabIndex = 3;
            uiButton1.Text = "plc连接";
            uiButton1.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton1.Click += uiButton1_Click;
            // 
            // uiButton2
            // 
            uiButton2.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton2.Location = new Point(312, 96);
            uiButton2.MinimumSize = new Size(1, 1);
            uiButton2.Name = "uiButton2";
            uiButton2.Size = new Size(100, 51);
            uiButton2.TabIndex = 4;
            uiButton2.Text = "错误复位";
            uiButton2.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton2.MouseDown += uiButton2_MouseDown;
            uiButton2.MouseUp += uiButton2_MouseUp;
            // 
            // uiButton3
            // 
            uiButton3.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton3.Location = new Point(179, 96);
            uiButton3.MinimumSize = new Size(1, 1);
            uiButton3.Name = "uiButton3";
            uiButton3.Size = new Size(100, 51);
            uiButton3.TabIndex = 5;
            uiButton3.Text = "断开连接";
            uiButton3.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton3.Click += uiButton3_Click;
            // 
            // uiLabel6
            // 
            uiLabel6.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiLabel6.ForeColor = Color.FromArgb(48, 48, 48);
            uiLabel6.Location = new Point(17, 46);
            uiLabel6.Name = "uiLabel6";
            uiLabel6.Size = new Size(100, 23);
            uiLabel6.TabIndex = 6;
            uiLabel6.Text = "连接状态：";
            // 
            // uiLabel7
            // 
            uiLabel7.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiLabel7.ForeColor = Color.FromArgb(48, 48, 48);
            uiLabel7.Location = new Point(99, 46);
            uiLabel7.Name = "uiLabel7";
            uiLabel7.Size = new Size(100, 23);
            uiLabel7.TabIndex = 7;
            uiLabel7.Text = "未连接";
            // 
            // uiLabel8
            // 
            uiLabel8.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiLabel8.ForeColor = Color.FromArgb(48, 48, 48);
            uiLabel8.Location = new Point(205, 46);
            uiLabel8.Name = "uiLabel8";
            uiLabel8.Size = new Size(49, 18);
            uiLabel8.TabIndex = 8;
            uiLabel8.Text = "手动";
            uiLabel8.Click += uiLabel8_Click;
            // 
            // uiButton9
            // 
            uiButton9.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton9.Location = new Point(312, 153);
            uiButton9.MinimumSize = new Size(1, 1);
            uiButton9.Name = "uiButton9";
            uiButton9.Size = new Size(100, 35);
            uiButton9.TabIndex = 9;
            uiButton9.Text = "单点敲击";
            uiButton9.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton9.Click += uiButton9_Click;
            // 
            // button1
            // 
            button1.BackColor = Color.CornflowerBlue;
            button1.ForeColor = Color.White;
            button1.Location = new Point(172, 153);
            button1.Name = "button1";
            button1.Size = new Size(107, 35);
            button1.TabIndex = 19;
            button1.Text = "停止采集";
            button1.UseVisualStyleBackColor = false;
            button1.Click += button1_Click;
            // 
            // uiButton4
            // 
            uiButton4.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton4.Location = new Point(25, 144);
            uiButton4.MinimumSize = new Size(1, 1);
            uiButton4.Name = "uiButton4";
            uiButton4.Size = new Size(125, 44);
            uiButton4.TabIndex = 20;
            uiButton4.Text = "手自动切换";
            uiButton4.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton4.Click += uiButton4_Click;
            // 
            // uiButton5
            // 
            uiButton5.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton5.Location = new Point(303, 38);
            uiButton5.MinimumSize = new Size(1, 1);
            uiButton5.Name = "uiButton5";
            uiButton5.Size = new Size(125, 44);
            uiButton5.TabIndex = 21;
            uiButton5.Text = "自动启停";
            uiButton5.TipsFont = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiButton5.Click += uiButton5_Click;
            // 
            // Form1
            // 
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(904, 717);
            Controls.Add(uiButton5);
            Controls.Add(uiButton4);
            Controls.Add(button1);
            Controls.Add(uiButton9);
            Controls.Add(uiLabel8);
            Controls.Add(uiLabel7);
            Controls.Add(uiLabel6);
            Controls.Add(uiButton3);
            Controls.Add(uiButton2);
            Controls.Add(uiButton1);
            Controls.Add(uiListBox1);
            Controls.Add(uiGroupBox2);
            Controls.Add(uiGroupBox1);
            Name = "Form1";
            Text = "Form1";
            ZoomScaleRect = new Rectangle(15, 15, 904, 717);
            Load += Form1_Load;
            uiGroupBox1.ResumeLayout(false);
            uiGroupBox4.ResumeLayout(false);
            uiGroupBox5.ResumeLayout(false);
            uiGroupBox3.ResumeLayout(false);
            uiGroupBox2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Sunny.UI.UIGroupBox uiGroupBox1;
        private Sunny.UI.UIGroupBox uiGroupBox2;
        private Sunny.UI.UIListBox uiListBox1;
        private Sunny.UI.UIButton uiButton1;
        private Sunny.UI.UIButton uiButton2;
        private Sunny.UI.UIGroupBox uiGroupBox4;
        private Sunny.UI.UIGroupBox uiGroupBox5;
        private Sunny.UI.UIGroupBox uiGroupBox3;
        private Sunny.UI.UIButton uiButton3;
        private Sunny.UI.UISymbolButton uiSymbolButton1;
        private Sunny.UI.UISymbolButton uiSymbolButton2;
        private Sunny.UI.UISymbolButton uiSymbolButton3;
        private Sunny.UI.UISymbolButton uiSymbolButton5;
        private Sunny.UI.UISymbolButton uiSymbolButton4;
        private Sunny.UI.UISymbolButton uiSymbolButton6;
        private Sunny.UI.UITextBox uiTextBox4;
        private Sunny.UI.UILabel uiLabel5;
        private Sunny.UI.UILabel uiLabel3;
        private Sunny.UI.UIButton uiButton8;
        private PictureBox pictureBox1;
        private Sunny.UI.UIButton uiButton7;
        private Sunny.UI.UITextBox uiTextBox5;
        private Sunny.UI.UIComboBox cbMicrophones;
        private Sunny.UI.UILabel uiLabel6;
        private Sunny.UI.UILabel uiLabel7;
        private Sunny.UI.UILabel uiLabel8;
        private Sunny.UI.UITextBox uiTextBox6;
        private Sunny.UI.UILabel uiLabel9;
        private Sunny.UI.UIButton uiButton9;
        private Button button1;
        private Sunny.UI.UIButton uiButton4;
        private Sunny.UI.UIButton uiButton5;
    }
}
