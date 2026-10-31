using System.Text;

namespace PoEformance.Game.Files;

/// <summary>
/// An action file (<c>.act</c>) - which animation each of a character's actions plays in each STANCE - as far as a stance's idle needs it:
/// the file it extends, its default wildcard, and the tree of stances, each with its own wildcard, its actions' first animation names, the
/// tags and timelines they carry, its Base timeline and the replace rules its tags name.
/// </summary>
/// <remarks>
/// READ FROM THE PLAYER'S OWN FILES (v0.1.397; the 16:16 capture in The Red Vale writes all three whole): IntFour.act names its
/// <c>parent "Metadata/Characters/CharacterFemale.act"</c>, which names Character.act, whose block opens with the default wildcard
/// (<c>"unarmed"</c>) and holds the actions - <c>Idle = "idle_*_01" timeline "…/base_unarmed.atl" tag Base tag DemonFormOverride tag Idle;</c> -
/// and a stance per held combination, nested where one plays another's animations: <c>stance Wand "wand_unarmed" { … stance
/// WandSpiritShield "wand_shield" { … } }</c>, a stance's <c>with_tag Base timeline "…/base_wand.atl";</c> moving every action tagged Base
/// onto its timeline. A <c>*</c> in an animation's name is the nearest wildcard up the tree. A statement ends at <c>;</c>, or where its
/// block does (<c>append_persistent PersistentIdleFemale</c>); a quoted value may span lines; <c>//</c> starts a comment and <c>/* */</c>
/// encloses one (Character.act shuts its whole Claw stance away so). No reference
/// reads the format (poe_data_tools has no parser for it).
/// </remarks>
public sealed class ActFile
{
    private ActFile(string parent, string wildcard, ActStance root, IReadOnlyList<string> faults)
    {
        Parent = parent;
        Wildcard = wildcard;
        Root = root;
        Faults = faults;
    }

    /// <summary>Nothing read.</summary>
    public static ActFile None { get; } = new(string.Empty, string.Empty, new ActStance(string.Empty, string.Empty, null), ["nothing to read"]);

    /// <summary>The .act this one extends, or empty.</summary>
    public string Parent { get; }

    /// <summary>The default wildcard, written before the file's block - <c>unarmed</c> - or empty.</summary>
    public string Wildcard { get; }

    /// <summary>The file's block: its actions and its stances.</summary>
    public ActStance Root { get; }

    /// <summary>What could not be read, or none.</summary>
    public IReadOnlyList<string> Faults { get; }

    /// <summary>Whether anything was read.</summary>
    public bool Ready => Root.Actions.Count > 0 || Root.Stances.Count > 0 || Parent.Length > 0;

    /// <summary>Reads the bytes of an .act (UTF-16 with its mark, as the game ships it, or UTF-8).</summary>
    public static ActFile Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads an .act's text.</summary>
    public static ActFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<string> tokens = Tokens(text);
        var faults = new List<string>();
        string parent = string.Empty, wildcard = string.Empty;
        var root = new ActStance(string.Empty, string.Empty, null);
        var at = 0;
        while (at < tokens.Count)
        {
            string one = tokens[at];
            if (one == "parent" && at + 1 < tokens.Count && Quoted(tokens[at + 1]) is { } up)
            {
                parent = up;
                at += 2;
            }
            else if (one == "animated_object" && at + 1 < tokens.Count && Quoted(tokens[at + 1]) is not null)
            {
                at += 2;
            }
            else if (Quoted(one) is { } said && at + 1 < tokens.Count && tokens[at + 1] == "{")
            {
                wildcard = said;
                at = Body(tokens, at + 2, root, faults);
            }
            else if (one == "{")
            {
                at = Body(tokens, at + 1, root, faults);
            }
            else
            {
                at++;
            }
        }

        return new ActFile(parent, wildcard, root, faults);
    }

    /// <summary>A block's statements into a stance, from the token after its brace; returns the token after its closing brace.</summary>
    private static int Body(List<string> tokens, int at, ActStance stance, List<string> faults)
    {
        while (at < tokens.Count)
        {
            string one = tokens[at];
            if (one == "}")
            {
                return at + 1;
            }

            if (one == "stance" && at + 1 < tokens.Count && Quoted(tokens[at + 1]) is null && tokens[at + 1] is not ("{" or "}" or ";" or "="))
            {
                string name = tokens[at + 1];
                at += 2;
                string wildcard = at < tokens.Count && Quoted(tokens[at]) is { } said ? said : string.Empty;
                if (wildcard.Length > 0)
                {
                    at++;
                }

                if (at >= tokens.Count || tokens[at] != "{")
                {
                    faults.Add($"stance {name}: no block");
                    continue;
                }

                var child = new ActStance(name, wildcard, stance);
                stance.Stances.Add(child);
                at = Body(tokens, at + 1, child, faults);
                continue;
            }

            // ONE STATEMENT: up to its ';', or to the end of its block (not consumed); a stray block inside it is stepped over whole.
            var statement = new List<string>();
            while (at < tokens.Count && tokens[at] is not (";" or "}"))
            {
                if (tokens[at] == "{")
                {
                    at = Skip(tokens, at + 1);
                    continue;
                }

                statement.Add(tokens[at++]);
            }

            if (at < tokens.Count && tokens[at] == ";")
            {
                at++;
            }

            Take(statement, stance);
        }

        faults.Add($"stance {(stance.Name.Length > 0 ? stance.Name : "(the file's block)")}: no closing brace");
        return at;
    }

    /// <summary>One statement into a stance: an action (<c>Name = "anim" timeline "…" tag A;</c>), a Base timeline, or a tag's replace rule.</summary>
    private static void Take(List<string> statement, ActStance stance)
    {
        if (statement.Count >= 3 && statement[1] == "=" && Quoted(statement[0]) is null)
        {
            string animation = Quoted(statement[2]) ?? string.Empty;
            string timeline = string.Empty;
            var tags = new List<string>();
            for (var one = 3; one + 1 < statement.Count; one++)
            {
                if (statement[one] == "timeline" && Quoted(statement[one + 1]) is { } path)
                {
                    timeline = path;
                }
                else if (statement[one] == "tag" && Quoted(statement[one + 1]) is null)
                {
                    tags.Add(statement[one + 1]);
                }
            }

            stance.Actions[statement[0]] = new ActAction(animation, timeline, tags);
            return;
        }

        if (statement.Count >= 3 && statement[0] == "with_tag")
        {
            string tag = statement[1];
            for (var one = 2; one + 1 < statement.Count; one++)
            {
                if (statement[one] == "timeline" && Quoted(statement[one + 1]) is { } path && tag == "Base")
                {
                    stance.BaseTimeline = path;
                }
                else if (statement[one] == "replace" && one + 2 < statement.Count
                    && Quoted(statement[one + 1]) is { } from && Quoted(statement[one + 2]) is { } to)
                {
                    stance.Replaces.Add((tag, from, to));
                }
            }
        }
    }

    /// <summary>Past a block whose opening brace is behind <paramref name="at"/>, nested ones included.</summary>
    private static int Skip(List<string> tokens, int at)
    {
        for (var depth = 1; at < tokens.Count; at++)
        {
            if (tokens[at] == "{")
            {
                depth++;
            }
            else if (tokens[at] == "}" && --depth == 0)
            {
                return at + 1;
            }
        }

        return at;
    }

    /// <summary>A quoted token's text, or null where the token is not quoted. Tokens keep their opening quote to say so.</summary>
    private static string? Quoted(string token) => token.Length > 0 && token[0] == '"' ? token[1..] : null;

    /// <summary>The text cut into words, quoted strings (kept with their opening quote, the closing one dropped) and <c>{ } = ;</c>.</summary>
    private static List<string> Tokens(string text)
    {
        var tokens = new List<string>();
        var word = new StringBuilder();
        void Flush()
        {
            if (word.Length > 0)
            {
                tokens.Add(word.ToString());
                word.Clear();
            }
        }

        for (var at = 0; at < text.Length; at++)
        {
            char one = text[at];
            if (one == '/' && at + 1 < text.Length && text[at + 1] == '/')
            {
                Flush();
                while (at < text.Length && text[at] != '\n')
                {
                    at++;
                }
            }
            else if (one == '/' && at + 1 < text.Length && text[at + 1] == '*')
            {
                // A BLOCK COMMENT: Character.act shuts the whole Claw stance away in one (/*stance Claw "claw_unarmed" … }*/).
                Flush();
                int end = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                at = end < 0 ? text.Length : end + 1;
            }
            else if (one == '"')
            {
                Flush();
                int end = text.IndexOf('"', at + 1);
                end = end < 0 ? text.Length : end;
                tokens.Add("\"" + text[(at + 1)..end]);
                at = end;
            }
            else if (one is '{' or '}' or '=' or ';')
            {
                Flush();
                tokens.Add(one.ToString());
            }
            else if (char.IsWhiteSpace(one) || one == '﻿')
            {
                Flush();
            }
            else
            {
                word.Append(one);
            }
        }

        Flush();
        return tokens;
    }
}

/// <summary>One action of a stance: the animation its value names first (a <c>*</c> standing for the stance's wildcard), its own timeline and tags.</summary>
public sealed record ActAction(string Animation, string Timeline, IReadOnlyList<string> Tags);

/// <summary>One stance of an .act and the stances under it - see <see cref="ActFile"/>.</summary>
public sealed class ActStance
{
    /// <summary>A stance read.</summary>
    /// <param name="name">Its name - <c>WandSpiritShield</c> - or empty for the file's block.</param>
    /// <param name="wildcard">The wildcard it writes after its name - <c>wand_shield</c> - or empty where it takes its parent's.</param>
    /// <param name="up">The stance it is written in, or null for the file's block.</param>
    public ActStance(string name, string wildcard, ActStance? up)
    {
        Name = name;
        Wildcard = wildcard;
        Up = up;
    }

    /// <summary>Its name, or empty for the file's block.</summary>
    public string Name { get; }

    /// <summary>Its own wildcard, or empty.</summary>
    public string Wildcard { get; set; }

    /// <summary>The stance it is written in, or null.</summary>
    public ActStance? Up { get; set; }

    /// <summary>Its own actions by name - <c>Idle</c>, <c>Idle::town</c>.</summary>
    public Dictionary<string, ActAction> Actions { get; } = new(StringComparer.Ordinal);

    /// <summary>The stances written in it.</summary>
    public List<ActStance> Stances { get; } = [];

    /// <summary>The timeline its <c>with_tag Base timeline</c> moves every action tagged Base onto, or empty.</summary>
    public string BaseTimeline { get; set; } = string.Empty;

    /// <summary>Its tags' replace rules - <c>with_tag ClassSpecific replace "_strdex_" "_int_"</c> - in order.</summary>
    public List<(string Tag, string From, string To)> Replaces { get; } = [];

    /// <summary>The stance of a name in this one's tree, itself included, or null.</summary>
    public ActStance? Find(string name)
    {
        if (string.Equals(Name, name, StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        foreach (ActStance one in Stances)
        {
            if (one.Find(name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The action of a name here or in the nearest stance up the tree that has one, with the stance it was found in.</summary>
    public (ActAction? Action, ActStance? In) ActionOf(string name)
    {
        for (ActStance? one = this; one is not null; one = one.Up)
        {
            if (one.Actions.TryGetValue(name, out ActAction? found))
            {
                return (found, one);
            }
        }

        return (null, null);
    }

    /// <summary>The nearest wildcard up the tree, this one's first; <paramref name="fallback"/> - the file's default - where none writes one.</summary>
    public string WildcardOf(string fallback)
    {
        for (ActStance? one = this; one is not null; one = one.Up)
        {
            if (one.Wildcard.Length > 0)
            {
                return one.Wildcard;
            }
        }

        return fallback;
    }

    /// <summary>The nearest Base timeline up the tree, this one's first, or empty.</summary>
    public string BaseTimelineOf()
    {
        for (ActStance? one = this; one is not null; one = one.Up)
        {
            if (one.BaseTimeline.Length > 0)
            {
                return one.BaseTimeline;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Another file's block taken into this one, as a file extending another takes its parent's: its actions over these, its stances into
    /// those of the same name or beside them, its Base timeline and wildcard where it writes them, its replace rules after these.
    /// </summary>
    public void Take(ActStance over)
    {
        ArgumentNullException.ThrowIfNull(over);
        foreach ((string name, ActAction action) in over.Actions)
        {
            Actions[name] = action;
        }

        if (over.BaseTimeline.Length > 0)
        {
            BaseTimeline = over.BaseTimeline;
        }

        if (over.Wildcard.Length > 0)
        {
            Wildcard = over.Wildcard;
        }

        Replaces.AddRange(over.Replaces);
        foreach (ActStance child in over.Stances)
        {
            if (Stances.FirstOrDefault(one => string.Equals(one.Name, child.Name, StringComparison.OrdinalIgnoreCase)) is { } same)
            {
                same.Take(child);
            }
            else
            {
                child.Up = this;
                Stances.Add(child);
            }
        }
    }
}
