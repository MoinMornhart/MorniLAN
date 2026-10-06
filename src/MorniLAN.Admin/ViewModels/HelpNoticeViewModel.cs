using MorniLAN.Admin.Server;

namespace MorniLAN.Admin.ViewModels;

/// <summary>Eine Hilfe-Anfrage im Banner der Übersicht.</summary>
public sealed class HelpNoticeViewModel(HelpNotice notice)
{
    public HelpNotice Notice { get; } = notice;

    public string Title => Notice.Request.ProfileName is { Length: > 0 } profile
        ? $"{profile} an „{Notice.MachineName}“ bittet um Hilfe"
        : $"„{Notice.MachineName}“ bittet um Hilfe";

    public string Time => $"um {Notice.Request.CreatedAt.ToLocalTime():HH:mm} Uhr";
}
