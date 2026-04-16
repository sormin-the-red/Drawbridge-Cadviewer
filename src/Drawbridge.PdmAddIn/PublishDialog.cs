namespace Drawbridge.PdmAddIn
{
    public partial class PublishDialog : Form
    {
        public string[] SelectedConfigurations { get; private set; } = Array.Empty<string>();
        public string[] SelectedFbxPaths       { get; private set; } = Array.Empty<string>();
        public string[] SelectedStlPaths       { get; private set; } = Array.Empty<string>();
        public string[] SelectedSkpPaths       { get; private set; } = Array.Empty<string>();
        public string   SelectedOwnerName      => (_ownerCombo.SelectedItem as OwnerOption)?.Name  ?? "";
        public string   SelectedOwnerEmail     => (_ownerCombo.SelectedItem as OwnerOption)?.Email ?? "";

        private readonly bool     _isAssembly;
        private readonly string[] _fbxVaultPaths;
        private readonly string[] _stlVaultPaths;
        private readonly string[] _skpVaultPaths;
        private CheckedListBox    _configList  = null!;
        private CheckedListBox    _fbxList     = null!;
        private CheckedListBox    _stlList     = null!;
        private CheckedListBox    _skpList     = null!;
        private Button            _btnPublish  = null!;
        private Button            _btnCancel   = null!;
        private Label             _lblFile     = null!;
        private Label             _lblVersion  = null!;
        private Label             _lblOwner    = null!;
        private ComboBox          _ownerCombo  = null!;
        private Label             _lblConfigs  = null!;
        private Label             _lblFbx      = null!;
        private Label             _lblStl      = null!;
        private Label             _lblSkp      = null!;

        private class OwnerOption
        {
            public string Name  { get; }
            public string Email { get; }
            public OwnerOption(string name, string email) { Name = name; Email = email; }
            public override string ToString() => Name;
        }

        private static readonly OwnerOption[] Owners =
        {
            new OwnerOption("",              ""),
            new OwnerOption("Adam Barth",    "abarth@thewoweffect.com"),
            new OwnerOption("Cole Harder",   "charder@thewoweffect.com"),
            new OwnerOption("Vance Wilson",  "vance@thewoweffect.com"),
            new OwnerOption("Drew Giovino",  "agiovino@thewoweffect.com"),
            new OwnerOption("Matt Benedict", "mattb@thewoweffect.com"),
        };

        public PublishDialog(string fileName, int version, bool isAssembly,
            string[] configurations, string[] fbxVaultPaths, string[] stlVaultPaths,
            string[] skpVaultPaths)
        {
            _isAssembly    = isAssembly;
            _fbxVaultPaths = fbxVaultPaths ?? Array.Empty<string>();
            _stlVaultPaths = stlVaultPaths ?? Array.Empty<string>();
            _skpVaultPaths = skpVaultPaths ?? Array.Empty<string>();
            InitializeComponent();

            _lblFile.Text    = fileName;
            _lblVersion.Text = $"PDM Version: {version}";

            bool showConfigs = isAssembly;
            _lblConfigs.Visible = _configList.Visible = showConfigs;
            if (showConfigs)
            {
                foreach (var cfg in configurations)
                    _configList.Items.Add(cfg, isChecked: true);
                if (configurations.Length == 0)
                    _lblConfigs.Text = "Configurations: all (none found in PDM)";
            }

            bool showFbx = _fbxVaultPaths.Length > 0;
            _lblFbx.Visible = _fbxList.Visible = showFbx;
            if (showFbx)
                foreach (var path in _fbxVaultPaths)
                    _fbxList.Items.Add(Path.GetFileName(path), isChecked: true);

            bool showStl = _stlVaultPaths.Length > 0;
            _lblStl.Visible = _stlList.Visible = showStl;
            if (showStl)
                foreach (var path in _stlVaultPaths)
                    _stlList.Items.Add(Path.GetFileName(path), isChecked: true);

            bool showSkp = _skpVaultPaths.Length > 0;
            _lblSkp.Visible = _skpList.Visible = showSkp;
            if (showSkp)
                foreach (var path in _skpVaultPaths)
                    _skpList.Items.Add(Path.GetFileName(path), isChecked: true);

            foreach (var o in Owners)
                _ownerCombo.Items.Add(o);
            _ownerCombo.SelectedIndex = 0;

            // ── Dynamic layout ────────────────────────────────────────────────────────
            const int margin = 12;
            int y = 64;

            _lblOwner.Location   = new Point(margin, y); y += 20;
            _ownerCombo.Location = new Point(margin, y);
            y += _ownerCombo.Height + 10;

            if (showConfigs)
            {
                _lblConfigs.Location = new Point(margin, y); y += 22;
                _configList.Location = new Point(margin, y);
                _configList.Height   = Math.Max(40, Math.Min(configurations.Length, 8) * 17 + 6);
                y += _configList.Height + 8;
            }

            if (showFbx)
            {
                _lblFbx.Location  = new Point(margin, y); y += 22;
                _fbxList.Location = new Point(margin, y);
                _fbxList.Height   = Math.Max(40, Math.Min(_fbxVaultPaths.Length, 5) * 17 + 6);
                y += _fbxList.Height + 8;
            }

            if (showStl)
            {
                _lblStl.Location  = new Point(margin, y); y += 22;
                _stlList.Location = new Point(margin, y);
                _stlList.Height   = Math.Max(40, Math.Min(_stlVaultPaths.Length, 5) * 17 + 6);
                y += _stlList.Height + 8;
            }

            if (showSkp)
            {
                _lblSkp.Location  = new Point(margin, y); y += 22;
                _skpList.Location = new Point(margin, y);
                _skpList.Height   = Math.Max(40, Math.Min(_skpVaultPaths.Length, 5) * 17 + 6);
                y += _skpList.Height + 8;
            }

            y += 4;
            _btnPublish.Location = new Point(ClientSize.Width - 176, y);
            _btnCancel.Location  = new Point(ClientSize.Width - 92,  y);
            ClientSize = new Size(ClientSize.Width, y + _btnPublish.Height + margin);
        }

        private void BtnPublish_Click(object sender, EventArgs e)
        {
            if (_isAssembly && _configList.CheckedItems.Count > 0)
            {
                var selected = new List<string>();
                foreach (var item in _configList.CheckedItems)
                    selected.Add(item.ToString()!);
                SelectedConfigurations = selected.ToArray();
            }

            if (_fbxList.CheckedItems.Count > 0)
            {
                var selected = new List<string>();
                for (int i = 0; i < _fbxList.Items.Count; i++)
                    if (_fbxList.GetItemChecked(i)) selected.Add(_fbxVaultPaths[i]);
                SelectedFbxPaths = selected.ToArray();
            }

            if (_stlList.CheckedItems.Count > 0)
            {
                var selected = new List<string>();
                for (int i = 0; i < _stlList.Items.Count; i++)
                    if (_stlList.GetItemChecked(i)) selected.Add(_stlVaultPaths[i]);
                SelectedStlPaths = selected.ToArray();
            }

            if (_skpList.CheckedItems.Count > 0)
            {
                var selected = new List<string>();
                for (int i = 0; i < _skpList.Items.Count; i++)
                    if (_skpList.GetItemChecked(i)) selected.Add(_skpVaultPaths[i]);
                SelectedSkpPaths = selected.ToArray();
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
