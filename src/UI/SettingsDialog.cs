using Godot;
using AIVoxelStudio.App;
using AIVoxelStudio.Core.Mc;

namespace AIVoxelStudio.UI;

public partial class SettingsDialog : Window
{
    private GameSettings _settings;
    private LineEdit _baseUrl;
    private LineEdit _apiKey;
    private LineEdit _model;
    private SpinBox _temperature;
    private OptionButton _reasoning;
    private LineEdit _extraBody;
    private SpinBox _toolRounds;
    private SpinBox _snapshots;
    private LineEdit _mcPath;
    private OptionButton _mcVersion;

    public event Action Saved;

    public override void _Ready()
    {
        Theme = UiTheme.Create();

        _baseUrl = GetNode<LineEdit>("Root/Box/Scroll/Grid/BaseUrl");
        _apiKey = GetNode<LineEdit>("Root/Box/Scroll/Grid/ApiKey");
        _model = GetNode<LineEdit>("Root/Box/Scroll/Grid/Model");
        _temperature = GetNode<SpinBox>("Root/Box/Scroll/Grid/Temperature");
        _reasoning = GetNode<OptionButton>("Root/Box/Scroll/Grid/Reasoning");
        _extraBody = GetNode<LineEdit>("Root/Box/Scroll/Grid/ExtraBody");
        _toolRounds = GetNode<SpinBox>("Root/Box/Scroll/Grid/ToolRounds");
        _snapshots = GetNode<SpinBox>("Root/Box/Scroll/Grid/Snapshots");
        _mcPath = GetNode<LineEdit>("Root/Box/Scroll/Grid/McPathRow/McPath");
        _mcVersion = GetNode<OptionButton>("Root/Box/Scroll/Grid/McVersion");
    }

    public void OnCloseRequested() => Hide();

    public void OnScanPressed() => RefreshVersions(_CurrentVersion());

    public void OnCancelPressed() => Hide();

    public void Bind(GameSettings settings)
    {
        _settings = settings;
        if (_baseUrl == null) return;
        _baseUrl.Text = settings.ApiBaseUrl;
        _apiKey.Text = settings.ApiKey;
        _model.Text = settings.Model;
        _temperature.Value = settings.Temperature;
        _reasoning.Select(ReasoningIndex(settings.ReasoningEffort));
        _extraBody.Text = settings.ExtraBodyJson;
        _toolRounds.Value = settings.MaxToolRounds;
        _snapshots.Value = settings.MaxSnapshotsPerRequest;
        _mcPath.Text = settings.McAssetsPath;
        RefreshVersions(settings.McVersion);
    }

    public void Open()
    {
        if (_settings != null) Bind(_settings);
        PopupCentered();
    }

    private static readonly string[] Efforts = { "", "low", "medium", "high", "max" };

    private static int ReasoningIndex(string effort)
    {
        int index = Array.IndexOf(Efforts, effort ?? "");
        return index < 0 ? 3 : index;
    }

    private void RefreshVersions(string preferred)
    {
        var root = McAssetsLocator.NormalizeRoot(_mcPath.Text);
        if (string.IsNullOrEmpty(root)) root = McAssetsLocator.GuessRoot();
        if (!string.IsNullOrEmpty(root)) _mcPath.Text = root;

        var versions = McAssetsLocator.ListVersions(root);
        _mcVersion.Clear();
        for (int i = 0; i < versions.Count; i++) _mcVersion.AddItem(versions[i]);

        if (versions.Count == 0)
        {
            _mcVersion.AddItem("（未找到版本）");
            _mcVersion.SetItemDisabled(0, true);
            return;
        }

        string selected = McAssetsLocator.ResolveVersion(root, preferred);
        for (int i = 0; i < versions.Count; i++)
        {
            if (versions[i] != selected) continue;
            _mcVersion.Selected = i;
            break;
        }
    }

    private string _CurrentVersion()
    {
        int index = _mcVersion.Selected;
        if (index < 0 || _mcVersion.IsItemDisabled(index)) return "";
        return _mcVersion.GetItemText(index);
    }

    public void Save()
    {
        if (_settings != null)
        {
            _settings.ApiBaseUrl = _baseUrl.Text.Trim();
            _settings.ApiKey = _apiKey.Text.Trim();
            _settings.Model = _model.Text.Trim();
            _settings.Temperature = (float)_temperature.Value;
            _settings.ReasoningEffort = Efforts[Mathf.Clamp(_reasoning.Selected, 0, Efforts.Length - 1)];
            _settings.ExtraBodyJson = _extraBody.Text.Trim();
            _settings.MaxToolRounds = (int)_toolRounds.Value;
            _settings.MaxSnapshotsPerRequest = (int)_snapshots.Value;
            _settings.McAssetsPath = McAssetsLocator.NormalizeRoot(_mcPath.Text);
            _settings.McVersion = _CurrentVersion();
            _settings.Persist();
        }
        Saved?.Invoke();
        Hide();
    }
}
