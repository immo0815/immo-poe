using ImGuiNET;
using PoEformance.Features;

namespace PoEformance.Overlay;

/// <summary>
/// The masked field a GitHub token is entered in, and the button that forgets it.
/// </summary>
/// <remarks>
/// ONE TOKEN, ASKED FOR WHEREVER IT IS MISSED. The update check reads the private repository's
/// releases with it and the boss-export upload writes the repository with it, so both the
/// Update page and the model pane's upload offer this field, and both save into the one
/// encrypted file (GitHubTokenStore).
///
/// Whether there is one is kept here and asked of the disk only when the store's generation
/// has moved: this is drawn every frame, and the other surface - or the config window, on its
/// own thread - may save or forget the token at any moment.
/// </remarks>
/// <param name="id">ImGui id suffix, unique per surface.</param>
/// <param name="hint">The greyed text in the empty field - which permission this surface needs.</param>
/// <param name="tooltip">How to make such a token.</param>
internal sealed class GitHubTokenField(string id, string hint, string tooltip)
{
    /// <summary>Longest token the field takes. Fine-grained tokens are about a hundred characters.</summary>
    private const uint TokenLength = 256;

    // Built once: the labels are drawn every frame, and an interpolation there is a string a frame.
    private readonly string _fieldLabel = $"##{id}";
    private readonly string _saveLabel = $"save token##{id}-save";
    private readonly string _forgetLabel = $"forget token##{id}-forget";

    private string _token = string.Empty;
    private int _seen = -1;
    private bool _has;

    /// <summary>Whether a token is saved, as of the store's last save or forget.</summary>
    public bool Has
    {
        get
        {
            int now = GitHubTokenStore.Generation;
            if (now != _seen)
            {
                _seen = now;
                _has = GitHubTokenStore.Has();
            }

            return _has;
        }
    }

    /// <summary>The masked field and its save button.</summary>
    /// <returns>What a press came to, in a sentence; null when nothing was pressed.</returns>
    public string? Ask()
    {
        ImGui.SetNextItemWidth(280f);
        ImGui.InputTextWithHint(_fieldLabel, hint, ref _token, TokenLength, ImGuiInputTextFlags.Password);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(_token.Trim().Length == 0);
        bool save = ImGui.Button(_saveLabel);
        ImGui.EndDisabled();

        if (!save)
        {
            return null;
        }

        bool saved = GitHubTokenStore.Save(_token, Dpapi.Protect);

        // Out of memory the moment it is written down - nothing keeps the plain text around.
        _token = string.Empty;
        return saved ? "token saved, encrypted to this Windows user" : "the token could not be saved";
    }

    /// <summary>The small button that deletes the saved token.</summary>
    /// <param name="disabled">Greyed out, e.g. while an upload is using the token.</param>
    /// <returns>What a press came to, in a sentence; null when nothing was pressed.</returns>
    public string? Forget(bool disabled = false)
    {
        ImGui.BeginDisabled(disabled);
        bool forget = ImGui.SmallButton(_forgetLabel);
        ImGui.EndDisabled();

        if (!forget)
        {
            return null;
        }

        return GitHubTokenStore.Forget() ? "token forgotten" : "the token could not be deleted";
    }
}
