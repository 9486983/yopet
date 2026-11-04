namespace yopet.Sdk;

public sealed class AgentApprovalConfig
{
    public string Title { get; init; } = "";

    public string? Description { get; init; }

    public string AgentName { get; init; } = "Agent";

    public string Activity { get; init; } = "";

    public string ApproveLabel { get; init; } = "Allow once";

    public string AlwaysLabel { get; init; } = "Always allow";

    public string DenyLabel { get; init; } = "Reject";

    public string OnceValue { get; init; } = "once";

    public string AlwaysValue { get; init; } = "always";

    public string RejectValue { get; init; } = "reject";
}
