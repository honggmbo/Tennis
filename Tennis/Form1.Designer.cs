namespace Tennis
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
			cbId = new ComboBox();
			cbCourt = new ComboBox();
			cbCourtNumber = new ComboBox();
			cbYear = new ComboBox();
			cbMonth = new ComboBox();
			cbDay = new ComboBox();
			cbStartTime = new ComboBox();
			cbEndTime = new ComboBox();
			button1 = new Button();
			button2 = new Button();
			reservationDelay = new CheckBox();
			label8 = new Label();
			label7 = new Label();
			label6 = new Label();
			label5 = new Label();
			label4 = new Label();
			label3 = new Label();
			label2 = new Label();
			label1 = new Label();
			SuspendLayout();
			// 
			// cbId
			// 
			cbId.FormattingEnabled = true;
			cbId.Location = new Point(94, 17);
			cbId.Name = "cbId";
			cbId.Size = new Size(121, 23);
			cbId.TabIndex = 8;
			// 
			// cbCourt
			// 
			cbCourt.FormattingEnabled = true;
			cbCourt.Location = new Point(94, 44);
			cbCourt.Name = "cbCourt";
			cbCourt.Size = new Size(121, 23);
			cbCourt.TabIndex = 9;
			// 
			// cbCourtNumber
			// 
			cbCourtNumber.FormattingEnabled = true;
			cbCourtNumber.Location = new Point(94, 72);
			cbCourtNumber.Name = "cbCourtNumber";
			cbCourtNumber.Size = new Size(121, 23);
			cbCourtNumber.TabIndex = 10;
			// 
			// cbYear
			// 
			cbYear.FormattingEnabled = true;
			cbYear.Location = new Point(94, 99);
			cbYear.Name = "cbYear";
			cbYear.Size = new Size(121, 23);
			cbYear.TabIndex = 11;
			// 
			// cbMonth
			// 
			cbMonth.FormattingEnabled = true;
			cbMonth.Location = new Point(94, 125);
			cbMonth.Name = "cbMonth";
			cbMonth.Size = new Size(121, 23);
			cbMonth.TabIndex = 12;
			// 
			// cbDay
			// 
			cbDay.FormattingEnabled = true;
			cbDay.Location = new Point(94, 152);
			cbDay.Name = "cbDay";
			cbDay.Size = new Size(121, 23);
			cbDay.TabIndex = 13;
			// 
			// cbStartTime
			// 
			cbStartTime.FormattingEnabled = true;
			cbStartTime.Location = new Point(94, 181);
			cbStartTime.Name = "cbStartTime";
			cbStartTime.Size = new Size(121, 23);
			cbStartTime.TabIndex = 14;
			// 
			// cbEndTime
			// 
			cbEndTime.FormattingEnabled = true;
			cbEndTime.Location = new Point(94, 206);
			cbEndTime.Name = "cbEndTime";
			cbEndTime.Size = new Size(121, 23);
			cbEndTime.TabIndex = 15;
			// 
			// button1
			// 
			button1.Location = new Point(241, 48);
			button1.Name = "button1";
			button1.Size = new Size(98, 56);
			button1.TabIndex = 16;
			button1.Text = "Start";
			button1.UseVisualStyleBackColor = true;
			button1.Click += OnClickStart;
			// 
			// button2
			// 
			button2.Location = new Point(241, 112);
			button2.Name = "button2";
			button2.Size = new Size(98, 56);
			button2.TabIndex = 17;
			button2.Text = "Stop";
			button2.UseVisualStyleBackColor = true;
			button2.Click += OnClickStop;
			// 
			// reservationDelay
			// 
			reservationDelay.AutoSize = true;
			reservationDelay.Location = new Point(241, 185);
			reservationDelay.Name = "reservationDelay";
			reservationDelay.Size = new Size(78, 19);
			reservationDelay.TabIndex = 18;
			reservationDelay.Text = "예약 대기";
			reservationDelay.UseVisualStyleBackColor = true;
			// 
			// label8
			// 
			label8.AutoSize = true;
			label8.Location = new Point(20, 214);
			label8.Name = "label8";
			label8.Size = new Size(55, 15);
			label8.TabIndex = 26;
			label8.Text = "종료시간";
			label8.TextAlign = ContentAlignment.MiddleRight;
			// 
			// label7
			// 
			label7.AutoSize = true;
			label7.Location = new Point(20, 189);
			label7.Name = "label7";
			label7.Size = new Size(55, 15);
			label7.TabIndex = 25;
			label7.Text = "시작시간";
			label7.TextAlign = ContentAlignment.MiddleRight;
			// 
			// label6
			// 
			label6.AutoSize = true;
			label6.Location = new Point(20, 163);
			label6.Name = "label6";
			label6.Size = new Size(19, 15);
			label6.TabIndex = 24;
			label6.Text = "일";
			label6.TextAlign = ContentAlignment.MiddleRight;
			// 
			// label5
			// 
			label5.AutoSize = true;
			label5.Location = new Point(20, 133);
			label5.Name = "label5";
			label5.Size = new Size(19, 15);
			label5.TabIndex = 23;
			label5.Text = "월";
			label5.TextAlign = ContentAlignment.MiddleRight;
			// 
			// label4
			// 
			label4.AutoSize = true;
			label4.Location = new Point(20, 107);
			label4.Name = "label4";
			label4.Size = new Size(19, 15);
			label4.TabIndex = 22;
			label4.Text = "년";
			label4.TextAlign = ContentAlignment.MiddleRight;
			// 
			// label3
			// 
			label3.AutoSize = true;
			label3.Location = new Point(20, 80);
			label3.Name = "label3";
			label3.Size = new Size(55, 15);
			label3.TabIndex = 21;
			label3.Text = "코트번호";
			label3.TextAlign = ContentAlignment.MiddleRight;
			// 
			// label2
			// 
			label2.AutoSize = true;
			label2.ImageAlign = ContentAlignment.MiddleRight;
			label2.Location = new Point(20, 52);
			label2.Name = "label2";
			label2.Size = new Size(31, 15);
			label2.TabIndex = 20;
			label2.Text = "코트";
			label2.TextAlign = ContentAlignment.MiddleRight;
			// 
			// label1
			// 
			label1.AutoSize = true;
			label1.Location = new Point(20, 25);
			label1.Name = "label1";
			label1.Size = new Size(43, 15);
			label1.TabIndex = 19;
			label1.Text = "아이디";
			label1.TextAlign = ContentAlignment.MiddleRight;
			// 
			// Form1
			// 
			AutoScaleDimensions = new SizeF(7F, 15F);
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new Size(370, 247);
			Controls.Add(label8);
			Controls.Add(label7);
			Controls.Add(label6);
			Controls.Add(label5);
			Controls.Add(label4);
			Controls.Add(label3);
			Controls.Add(label2);
			Controls.Add(label1);
			Controls.Add(reservationDelay);
			Controls.Add(button2);
			Controls.Add(button1);
			Controls.Add(cbEndTime);
			Controls.Add(cbStartTime);
			Controls.Add(cbDay);
			Controls.Add(cbMonth);
			Controls.Add(cbYear);
			Controls.Add(cbCourtNumber);
			Controls.Add(cbCourt);
			Controls.Add(cbId);
			Name = "Form1";
			Text = "예약";
			ResumeLayout(false);
			PerformLayout();
		}

		#endregion
		private ComboBox cbId;
		private ComboBox cbCourt;
		private ComboBox cbCourtNumber;
		private ComboBox cbYear;
		private ComboBox cbMonth;
		private ComboBox cbDay;
		private ComboBox cbStartTime;
		private ComboBox cbEndTime;
		private Button button1;
		private Button button2;
		private CheckBox reservationDelay;
		private Label label8;
		private Label label7;
		private Label label6;
		private Label label5;
		private Label label4;
		private Label label3;
		private Label label2;
		private Label label1;
	}
}
