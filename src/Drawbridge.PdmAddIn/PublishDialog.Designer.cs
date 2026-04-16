namespace Drawbridge.PdmAddIn
{
    partial class PublishDialog
    {
        private System.ComponentModel.IContainer? components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this._lblFile    = new System.Windows.Forms.Label();
            this._lblVersion = new System.Windows.Forms.Label();
            this._lblOwner   = new System.Windows.Forms.Label();
            this._ownerCombo = new System.Windows.Forms.ComboBox();
            this._lblConfigs = new System.Windows.Forms.Label();
            this._configList = new System.Windows.Forms.CheckedListBox();
            this._lblFbx     = new System.Windows.Forms.Label();
            this._fbxList    = new System.Windows.Forms.CheckedListBox();
            this._lblStl     = new System.Windows.Forms.Label();
            this._stlList    = new System.Windows.Forms.CheckedListBox();
            this._lblSkp     = new System.Windows.Forms.Label();
            this._skpList    = new System.Windows.Forms.CheckedListBox();
            this._btnPublish = new System.Windows.Forms.Button();
            this._btnCancel  = new System.Windows.Forms.Button();
            this.SuspendLayout();

            // Form
            this.Text            = "Publish to Drawbridge";
            this.ClientSize      = new System.Drawing.Size(392, 320);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox     = false;
            this.MinimizeBox     = false;
            this.StartPosition   = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Font            = new System.Drawing.Font("Segoe UI", 9F);

            this._lblFile.Location = new System.Drawing.Point(12, 16);
            this._lblFile.Size     = new System.Drawing.Size(368, 20);
            this._lblFile.Font     = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);

            this._lblVersion.Location = new System.Drawing.Point(12, 40);
            this._lblVersion.Size     = new System.Drawing.Size(368, 18);

            this._lblOwner.Text = "Product owner:";
            this._lblOwner.Size = new System.Drawing.Size(368, 18);

            this._ownerCombo.Size          = new System.Drawing.Size(240, 24);
            this._ownerCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;

            this._lblConfigs.Text = "Configurations to include:";
            this._lblConfigs.Size = new System.Drawing.Size(368, 16);

            this._configList.Size         = new System.Drawing.Size(368, 120);
            this._configList.CheckOnClick = true;

            this._lblFbx.Text = "FBX files in folder:";
            this._lblFbx.Size = new System.Drawing.Size(368, 16);

            this._fbxList.Size         = new System.Drawing.Size(368, 80);
            this._fbxList.CheckOnClick = true;

            this._lblStl.Text = "STL files in folder:";
            this._lblStl.Size = new System.Drawing.Size(368, 16);

            this._stlList.Size         = new System.Drawing.Size(368, 80);
            this._stlList.CheckOnClick = true;

            this._lblSkp.Text = "SKP files in folder:";
            this._lblSkp.Size = new System.Drawing.Size(368, 16);

            this._skpList.Size         = new System.Drawing.Size(368, 80);
            this._skpList.CheckOnClick = true;

            this._btnPublish.Text   = "Publish";
            this._btnPublish.Size   = new System.Drawing.Size(80, 28);
            this._btnPublish.Click += new System.EventHandler(this.BtnPublish_Click);

            this._btnCancel.Text   = "Cancel";
            this._btnCancel.Size   = new System.Drawing.Size(80, 28);
            this._btnCancel.Click += new System.EventHandler(this.BtnCancel_Click);

            this.Controls.AddRange(new System.Windows.Forms.Control[] {
                this._lblFile, this._lblVersion,
                this._lblOwner, this._ownerCombo,
                this._lblConfigs, this._configList,
                this._lblFbx, this._fbxList,
                this._lblStl, this._stlList,
                this._lblSkp, this._skpList,
                this._btnPublish, this._btnCancel,
            });

            this.ResumeLayout(false);
        }
    }
}
