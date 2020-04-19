using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Winp.Packages;

namespace Winp.Forms
{
    public partial class ServiceForm : Form
    {
        private static readonly string ConfigurationPath =
            Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(Application.ExecutablePath) + ".json");

        private readonly IReadOnlyList<Instance> _instances = new[]
            {new Instance(new MariaDbPackage()), new Instance(new NginxPackage()), new Instance(new PhpPackage())};

        private readonly IReadOnlyList<IPackage> _packages = new IPackage[]
            {new MariaDbPackage(), new NginxPackage(), new PhpPackage(), new PhpMyAdminPackage()};

        private Configuration.ApplicationConfig _configuration;
        private readonly TaskScheduler _scheduler;

        public ServiceForm()
        {
            InitializeComponent();

            _configuration = ConfigurationLoad();
            _scheduler = TaskScheduler.FromCurrentSynchronizationContext();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Allow closing form is all services are stopped
            if (!_instances.Any(instance => instance.IsRunning))
                return;

            // Otherwise stop them before closing form
            ExecuteStop().ContinueWith(task => Close(), _scheduler);

            e.Cancel = true;
        }

        private static Configuration.ApplicationConfig ConfigurationLoad()
        {
            if (!File.Exists(ConfigurationPath))
                return default;

            using var stream = new FileStream(ConfigurationPath, FileMode.Open, FileAccess.Read);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            return JsonConvert.DeserializeObject<Configuration.ApplicationConfig>(reader.ReadToEnd());
        }

        private static void ConfigurationSave(Configuration.ApplicationConfig configuration)
        {
            using var stream = new FileStream(ConfigurationPath, FileMode.Create, FileAccess.Write);
            using var writer = new StreamWriter(stream, Encoding.UTF8);

            writer.Write(JsonConvert.SerializeObject(configuration, Formatting.Indented));
        }

        private void ConfigureButton_Click(object sender, EventArgs e)
        {
            var form = new ConfigurationForm(_configuration, configuration =>
            {
                ConfigurationSave(configuration);

                _configuration = configuration;
            });

            form.Show(this);
        }

        private void InstallButton_Click(object sender, EventArgs e)
        {
            var imageList = _statusImageList;
            var label = _installStatusLabel;

            InstallRun().ContinueWith(install =>
            {
                TimeSpan pause;

                if (install.IsFaulted && install.Exception != null)
                {
                    SetStatusLabel(label, imageList, Status.Failure, "Installation failed: " + install.Exception.Message);

                    pause = TimeSpan.FromSeconds(3);
                }
                else if (install.Result != null)
                {
                    SetStatusLabel(label, imageList, Status.Failure, "Installation failed: " + install.Result);

                    pause = TimeSpan.FromSeconds(3);
                }
                else
                    pause = TimeSpan.Zero;

                Task.Delay(pause).ContinueWith(delay => InstallRefresh(), _scheduler);
            }, _scheduler);
        }

        private void ServiceForm_Shown(object sender, EventArgs e)
        {
            InstallRefresh();
            ExecuteRefresh();
        }

        private void ExecuteStartButton_Click(object sender, EventArgs e)
        {
            Task.Run(ExecuteStart);
        }

        private void ExecuteStopButton_Click(object sender, EventArgs e)
        {
            Task.Run(ExecuteStop);
        }

        private void InstallRefresh()
        {
            var missing = new List<string>();

            foreach (var package in _packages)
            {
                if (!package.IsReady(_configuration))
                    missing.Add(package.Name);
            }

            if (missing.Count > 0)
            {
                var message = "Missing packages: " + string.Join(", ", missing) + $" (click '{_installButton.Text}')";

                SetStatusLabel(_installStatusLabel, _statusImageList, Status.Notice, message);
            }
            else
                SetStatusLabel(_installStatusLabel, _statusImageList, Status.Success, "Packages installed");
        }

        private async Task<string?> InstallRun()
        {
            var imageList = _statusImageList;
            var label = _installStatusLabel;

            await ExecuteStop();

            SetStatusLabel(label, imageList, Status.Loading, "Downloading and configuring packages...");

            foreach (var package in _packages)
            {
                var message = await package.Install(_configuration);

                if (message != null)
                    return $"{package.Name}: {message}";
            }

            return null;
        }

        private void ExecuteRefresh()
        {
            var allExited = true;
            var anyExited = false;

            foreach (var instance in _instances)
            {
                allExited = allExited && !instance.IsRunning;
                anyExited = anyExited || !instance.IsRunning;
            }

            if (allExited)
                SetStatusLabel(_executeStatusLabel, _statusImageList, Status.Notice, "Services stopped");
            else if (anyExited)
                SetStatusLabel(_executeStatusLabel, _statusImageList, Status.Failure, "Process faulted");
            else
                SetStatusLabel(_executeStatusLabel, _statusImageList, Status.Success, "Services running");
        }

        private async Task ExecuteStart()
        {
            SetStatusLabel(_executeStatusLabel, _statusImageList, Status.Loading, "Starting services...");

            for (var i = 0; i < _instances.Count; ++i)
            {
                var instance = _instances[i];

                if (await instance.Start(_configuration, () => Task.Run(ExecuteRefresh)))
                    continue;

                for (var j = 0; j < i; ++j)
                    await _instances[j].Stop(_configuration);

                SetStatusLabel(_executeStatusLabel, _statusImageList, Status.Failure,
                    $"Failed starting process {instance.Package.Name}");

                return;
            }

            ExecuteRefresh();
        }

        private async Task ExecuteStop()
        {
            SetStatusLabel(_executeStatusLabel, _statusImageList, Status.Loading, "Stopping services...");

            foreach (var instance in _instances)
                await instance.Stop(_configuration);

            ExecuteRefresh();
        }

        private void SetStatusLabel(Label label, ImageList imageList, Status? status, string text)
        {
            new Task(() =>
            {
                const int space = 4;

                if (status.HasValue)
                {
                    label.ImageIndex = (int) status.Value;
                    label.Text = text;

                    label.AutoSize = true;

                    var width = label.Width;

                    label.AutoSize = false;

                    label.Width = imageList.ImageSize.Width + space + width;
                    label.Visible = true;
                }
                else
                    label.Visible = false;
            }).Start(_scheduler);
        }
    }
}