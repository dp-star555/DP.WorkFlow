namespace WindowsFormsApp1
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.modernButton1 = new ModernUI.WinForms.ModernButton();
            this.SuspendLayout();
            // 
            // modernButton1
            // 
            this.modernButton1.ButtonType = ModernUI.WinForms.ModernButtonType.Primary;
            this.modernButton1.Location = new System.Drawing.Point(24, 24);
            this.modernButton1.Name = "modernButton1";
            this.modernButton1.Size = new System.Drawing.Size(120, 38);
            this.modernButton1.TabIndex = 0;
            this.modernButton1.Text = "现代按钮";
            // 
            // Form1
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.modernButton1);
            this.Text = "ModernUI .NET Framework 4.8";
            this.ResumeLayout(false);
        }

        #endregion

        private ModernUI.WinForms.ModernButton modernButton1;
    }
}

