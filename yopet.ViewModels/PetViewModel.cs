using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using yopet.Core.Interfaces;
using yopet.Core.Models;
using yopet.Sdk;

namespace yopet.ViewModels;

public partial class PetViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly IPetdexService _petdexService;
    private readonly IDispatcherService _dispatcher;
    private readonly Random _random = new();
    private CancellationTokenSource? _pageCts;
    private int _dragGeneration;

    // ── 宠物悬浮提示（来自插件事件池） ──
    private readonly PluginEventPool? _eventPool;

    // ── 精灵图属性 ──

    [ObservableProperty]
    private string _petName = "";

    [ObservableProperty]
    private string _spritesheetPath = "";

    [ObservableProperty]
    private int _animFrameWidth = 192;

    [ObservableProperty]
    private int _animFrameHeight = 208;

    [ObservableProperty]
    private int _animColumns = 8;

    [ObservableProperty]
    private int _animRows = 9;

    [ObservableProperty]
    private double _animFrameDurationMs = 100.0;

    [ObservableProperty]
    private int _animCurrentRow;

    // ── 反应气泡 ──

    [ObservableProperty]
    private string _currentReaction = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBubbleVisible))]
    private bool _isReacting;

    // ── Agent 对话监测气泡 ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyBubbleVisible))]
    private bool _isShowingThought;

    [ObservableProperty]
    private string _thoughtText = "";

    [ObservableProperty]
    private string _thoughtAssistant = "";

    /// <summary>任一气泡可见（统一外部 Popup 绑定）</summary>
    public bool IsAnyBubbleVisible => IsReacting || IsShowingThought;

    /// <summary>当前宠物定义</summary>
    public PetDefinition? CurrentPet { get; private set; }

    /// <summary>已安装的 Petdex 宠物列表</summary>
    public List<PetDefinition> PetdexPets { get; private set; } = [];

    /// <summary>交互动作（右键菜单用，由插件注册）</summary>
    public List<PetActionConfig> Actions { get; } = [];

    /// <summary>文件拖放动作（径向菜单用）</summary>
    public List<FileActionConfig> FileActions => _fileActions;
    private readonly List<FileActionConfig> _fileActions = [];

    /// <summary>是否处于激活模式</summary>
    public bool IsActivated => ActivatedFileAction != null;

    /// <summary>显示 💡 指示灯（激活且无任务进行中；会话中显示 📋）</summary>
    public bool IsIndicatorVisible => IsActivated && !IsTaskRunning;

    /// <summary>指示灯文字：会话中 📋，普通激活 💡</summary>
    public string IndicatorText => IsSessionActive ? "📋" : "💡";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActivated))]
    [NotifyPropertyChangedFor(nameof(IsIndicatorVisible))]
    private FileActionConfig? _activatedFileAction;

    /// <summary>剪贴板写入回调（由 UI 层设置）</summary>
    public Action<string>? ClipboardSetText { get; set; }

    /// <summary>是否有任务正在执行（控制进度环显隐，隐藏 💡）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndicatorVisible))]
    private bool _isTaskRunning;

    /// <summary>取消任务回调（由 App 层设置，调用插件 host.CancelCurrentTask）</summary>
    public Action? CancelTaskCallback { get; set; }

    /// <summary>热重载插件回调（由 App 层设置，调用宿主重载并刷新动作列表）</summary>
    public Action? ReloadPluginsCallback { get; set; }

    /// <summary>
    /// 刷新插件注册的右键动作（热重载后调用，右键菜单下次打开即生效）。
    /// </summary>
    public void RefreshPluginActions(IEnumerable<PetActionConfig>? pluginActions)
    {
        Actions.Clear();
        if (pluginActions != null) Actions.AddRange(pluginActions);
    }

    /// <summary>刷新插件注册的文件动作（热重载后调用）</summary>
    public void RefreshFileActions(IEnumerable<FileActionConfig>? fileActions)
    {
        _fileActions.Clear();
        if (fileActions != null) _fileActions.AddRange(fileActions);
    }

    // ── 会话多步工作流 ──

    /// <summary>当前活跃会话（无则为 null）</summary>
    public ISession? CurrentSession { get; set; }

    /// <summary>是否有会话进行中</summary>
    public bool IsSessionActive => CurrentSession?.IsActive == true;

    /// <summary>结束会话回调（由 App 层设置，调用 host.CurrentSession.Cancel）</summary>
    public Action? EndSessionCallback { get; set; }

    [RelayCommand]
    private void CancelRunningTask()
    {
        CancelTaskCallback?.Invoke();
    }

    // ── 会话事件（由 App 层通过回调调用） ──

    /// <summary>会话启动时调用</summary>
    public void OnSessionStarted(ISession session)
    {
        CurrentSession = session;

        // 同步激活状态到 VM，使拖放路由能识别
        var match = _fileActions.FirstOrDefault(a =>
            a.Name.Equals(session.Title, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            ActivatedFileAction = match;

        OnPropertyChanged(nameof(IndicatorText));
        ShowFileDropInfo($"📋 {session.Title}", string.IsNullOrEmpty(session.Status)
            ? "会话已开始，拖入文件继续处理" : session.Status);
    }

    /// <summary>会话结束时调用</summary>
    public void OnSessionEnded()
    {
        CurrentSession = null;
        ActivatedFileAction = null;
        OnPropertyChanged(nameof(IndicatorText));
        ShowFileDropInfo("✅ 会话已结束", "多步工作流已完成，恢复常规模式。");
    }

    public PetViewModel(IConfigService configService, IDispatcherService dispatcher,
        IPetdexService petdexService,
        List<PetActionConfig>? pluginActions = null,
        List<FileActionConfig>? fileActions = null,
        PluginEventPool? eventPool = null)
    {
        _configService = configService;
        _dispatcher = dispatcher;
        _petdexService = petdexService;
        _eventPool = eventPool;

        // 加载插件注册的文件动作
        if (fileActions != null && fileActions.Count > 0)
            _fileActions.AddRange(fileActions);

        // 扫描已安装宠物
        ReloadPetdexPets();

        // 加载上次使用的宠物
        var cfg = configService.Config;
        ApplyPetById(cfg.CurrentPetId);
        _petName = string.IsNullOrEmpty(cfg.PetName) && CurrentPet != null
            ? CurrentPet.Name
            : cfg.PetName;

        // 合并插件注册的动作
        if (pluginActions != null && pluginActions.Count > 0)
        {
            var merged = new List<PetActionConfig>(Actions);
            merged.AddRange(pluginActions);
            Actions = merged;
        }

        // 恢复上次激活的默认操作
        RestoreActivatedAction();

        // 监听配置保存事件（动画速度立即生效）
        PetEvents.ConfigSaved += OnConfigSaved;
    }

    private void OnConfigSaved()
    {
        AnimFrameDurationMs = _configService.Config.AnimFrameDurationMs;
    }

    /// <summary>重新扫描 ~/.codex/pets/ + ~/.petdex/pets/</summary>
    [RelayCommand]
    public void ReloadPetdexPets()
    {
        PetdexPets = _petdexService.GetInstalledPetIds()
            .Select(id => _petdexService.ToPetDefinition(id))
            .Where(p => p != null)
            .Cast<PetDefinition>()
            .ToList();
        OnPropertyChanged(nameof(PetdexPets));
    }

    /// <summary>按 petdex:xxx 格式 ID 切换到宠物</summary>
    public void ApplyPetById(string petId)
    {
        if (!petId.StartsWith("petdex:"))
        {
            // 无有效 ID 时选第一个已安装宠物
            var first = PetdexPets.FirstOrDefault();
            if (first != null) { ApplyPetDefinition(first); }
            return;
        }

        var slug = petId["petdex:".Length..];
        var def = _petdexService.ToPetDefinition(slug);
        if (def != null) ApplyPetDefinition(def);
    }

    private void ApplyPetDefinition(PetDefinition pet)
    {
        CurrentPet = pet;
        PetName = pet.Name;
        // 先设为待机
        AnimCurrentRow = 0;
        // ★ 先更新尺寸，再换精灵图 —— SpritesheetView.OnSpritesheetChanged
        //   需要正确的 FrameWidth/FrameHeight 来解码新宠物的帧
        AnimFrameWidth = pet.FrameWidth;
        AnimFrameHeight = pet.FrameHeight;
        AnimColumns = pet.Columns;
        AnimRows = pet.Rows;
        SpritesheetPath = pet.SpritesheetPath;
        AnimFrameDurationMs = _configService.Config.AnimFrameDurationMs;
        AnimCurrentRow = 0;

        _configService.Config.CurrentPetId = pet.Id;
        _configService.Config.PetName = pet.Name;
        _configService.Save();
    }

    /// <summary>保存宠物窗口位置（下次启动恢复）</summary>
    public void SavePosition(double x, double y)
    {
        _configService.Config.PetWindowX = x;
        _configService.Config.PetWindowY = y;
        _configService.Save();
    }

    // ── 交互 ──

    [RelayCommand]
    private void SingleClick()
    {
        AnimCurrentRow = 3; // waving
        ResetAnimRowAfterDelay();
    }

    [RelayCommand]
    private async Task PerformAction(PetActionConfig? action)
    {
        if (action == null) return;

        if (action.ActionCallback != null)
        {
            AnimCurrentRow = 7; // running
            try { await action.ActionCallback(); }
            catch (Exception ex)
            {
                AnimCurrentRow = 5; // failed
                ShowFileDropInfo("⚠️ 插件错误", ex.Message);
            }
            ResetAnimRowAfterDelay();
            return;
        }

        ShowReaction(action.Reaction);
        ResetAnimRowAfterDelay();
    }

    /// <summary>激活一个文件动作为默认拖放操作</summary>
    public void ActivateAction(FileActionConfig action)
    {
        ActivatedFileAction = action;
        _configService.Config.ActivatedFileActionName = action.Name;
        _configService.Save();
        ShowFileDropInfo("📌 已锁定", $"「{action.Emoji} {action.Name}」\n拖文件将直接执行此操作，右键可解锁。");
    }

    /// <summary>解锁默认拖放操作</summary>
    public void DeactivateAction()
    {
        if (ActivatedFileAction == null) return;
        var name = ActivatedFileAction.Name;
        ActivatedFileAction = null;
        _configService.Config.ActivatedFileActionName = null;
        _configService.Save();
        ShowFileDropInfo("🔓 已解锁", $"「{name}」已取消锁定，拖文件将恢复弹出选项菜单。");
    }

    /// <summary>重启后恢复上次激活的操作</summary>
    private void RestoreActivatedAction()
    {
        var savedName = _configService.Config.ActivatedFileActionName;
        if (string.IsNullOrEmpty(savedName)) return;
        var match = _fileActions.FirstOrDefault(a =>
            a.Name.Equals(savedName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            ActivatedFileAction = match;
            // 不弹气泡，安静恢复
        }
    }

    [RelayCommand]
    private void SelectPet(PetDefinition? pet)
    {
        if (pet == null) return;
        ApplyPetDefinition(pet);
        ShowReaction("✨");
    }

    private async void ResetAnimRowAfterDelay()
    {
        await Task.Delay(1500);
        _dispatcher.Post(() => AnimCurrentRow = 0);
    }

    /// <summary>拖拽结束后 1.5s 恢复待机（含代际防冲突）</summary>
    public void ScheduleIdleAfterDrag()
    {
        var gen = Interlocked.Increment(ref _dragGeneration);
        Task.Delay(1500).ContinueWith(_ =>
        {
            if (gen == _dragGeneration)
                _dispatcher.Post(() => AnimCurrentRow = 0);
        });
    }

    public void ShowReaction(string reaction)
    {
        _dispatcher.Post(() =>
        {
            CurrentReaction = reaction;
            IsReacting = true;
        });
        Task.Delay(2000).ContinueWith(_ =>
            _dispatcher.Post(() => IsReacting = false));
        // 动画行复位：表情气泡消失时同时恢复待机
        Task.Delay(2000).ContinueWith(_ =>
            _dispatcher.Post(() => AnimCurrentRow = 0));
    }

    /// <summary>在对话气泡中显示信息，多行内容自动分页轮播（每批最多 3 行，2 秒切换）</summary>
    public void ShowFileDropInfo(string title, string info)
    {
        // 取消之前的轮播
        _pageCts?.Cancel();

        _dispatcher.Post(() =>
        {
            ThoughtAssistant = title;
            IsShowingThought = true;
        });

        var lines = info.Split('\n', StringSplitOptions.None);
        const int linesPerPage = 5;

        if (lines.Length <= linesPerPage)
        {
            // 短文本：一次性显示
            _dispatcher.Post(() => ThoughtText = info);
            Task.Delay(8000).ContinueWith(_ =>
                _dispatcher.Post(() => IsShowingThought = false));
            return;
        }

        // 长文本：分页轮播
        var pages = new List<string>();
        for (var i = 0; i < lines.Length; i += linesPerPage)
        {
            var page = string.Join("\n", lines.Skip(i).Take(linesPerPage));
            pages.Add(page);
        }

        _pageCts = new CancellationTokenSource();
        var ct = _pageCts.Token;
        var pageIndex = 0;

        // 显示第一页
        _dispatcher.Post(() => ThoughtText = pages[0]);

        // 启动轮播：每页 2.5 秒，结束后再展示 2 秒最后一页
        Task.Run(async () =>
        {
            for (var i = 1; i < pages.Count; i++)
            {
                try { await Task.Delay(2500, ct); }
                catch (TaskCanceledException) { return; }

                if (ct.IsCancellationRequested) return;
                var idx = i;
                _dispatcher.Post(() => ThoughtText = pages[idx]);
            }

            // 全部播完后等待 2 秒再隐藏
            try { await Task.Delay(3000, ct); }
            catch (TaskCanceledException) { return; }

            _dispatcher.Post(() => IsShowingThought = false);
        }, ct);
    }

    // ── 宠物悬浮提示（事件绑定与触发，展示由插件调用气泡组件完成） ──

    /// <summary>鼠标进入宠物：触发事件池悬浮进入事件（由 PetWindow 事件绑定调用）</summary>
    public void OnPetHoverEntered() => _eventPool?.Publish(EventNames.PetHoverEntered);

    /// <summary>鼠标离开宠物：触发事件池悬浮离开事件（由 PetWindow 事件绑定调用）</summary>
    public void OnPetHoverExited() => _eventPool?.Publish(EventNames.PetHoverExited);

    // ── 宠物单击/双击（事件绑定与触发，插件可订阅响应） ──

    /// <summary>单击宠物：触发事件池单击事件（由 PetWindow 识别后调用）</summary>
    public void OnPetClicked() => _eventPool?.Publish(EventNames.PetClicked);

    /// <summary>双击宠物：触发事件池双击事件（由 PetWindow 识别后调用）</summary>
    public void OnPetDoubleClicked() => _eventPool?.Publish(EventNames.PetDoubleClicked);

    public void Cleanup()
    {
        _pageCts?.Cancel();
        _pageCts?.Dispose();
        PetEvents.ConfigSaved -= OnConfigSaved;
    }
}
