namespace yopet.Sdk;

/// <summary>宠物动画动作 —— 插件通过此枚举控制宠物表现</summary>
/// <remarks>对应精灵图行号：0=idle,1=running-right,2=running-left,3=waving,4=jumping,5=failed,6=waiting,7=running,8=review</remarks>
public enum PetAnimation
{
    /// <summary>待机 / 呼吸</summary>
    Idle = 0,
    /// <summary>向右跑（活跃、正面反馈）</summary>
    RunningRight = 1,
    /// <summary>向左跑（返回、取消）</summary>
    RunningLeft = 2,
    /// <summary>挥手 / 打招呼</summary>
    Wave = 3,
    /// <summary>跳跃 / 惊喜反应</summary>
    Jump = 4,
    /// <summary>失败 / 沮丧</summary>
    Failed = 5,
    /// <summary>等待 / 空闲等待</summary>
    Waiting = 6,
    /// <summary>忙碌工作（处理中、计算、查询等耗时操作）</summary>
    Running = 7,
    /// <summary>审查代码（阅读、分析）</summary>
    Review = 8,
}
