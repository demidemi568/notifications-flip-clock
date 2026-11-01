using System.Drawing.Drawing2D;
using System.Globalization;

namespace FlipClock;

/// <summary>
/// Silhouette icons for the bubbles. The shapes are the path data from the Discord and Telegram SVGs
/// (svgrepo.com), turned into GDI+ paths so they can be drawn in any colour at any size.
/// </summary>
static class Icons
{
    // discord-icon-svgrepo-com.svg (viewBox 0 -28.5 256 256)
    const string Discord =
        "M216.856339,16.5966031 C200.285002,8.84328665 182.566144,3.2084988 164.041564,0 C161.766523,4.11318106 159.108624,9.64549908 157.276099,14.0464379 " +
        "C137.583995,11.0849896 118.072967,11.0849896 98.7430163,14.0464379 C96.9108417,9.64549908 94.1925838,4.11318106 91.8971895,0 " +
        "C73.3526068,3.2084988 55.6133949,8.86399117 39.0420583,16.6376612 C5.61752293,67.146514 -3.4433191,116.400813 1.08711069,164.955721 " +
        "C23.2560196,181.510915 44.7403634,191.567697 65.8621325,198.148576 C71.0772151,190.971126 75.7283628,183.341335 79.7352139,175.300261 " +
        "C72.104019,172.400575 64.7949724,168.822202 57.8887866,164.667963 C59.7209612,163.310589 61.5131304,161.891452 63.2445898,160.431257 " +
        "C105.36741,180.133187 151.134928,180.133187 192.754523,160.431257 C194.506336,161.891452 196.298154,163.310589 198.110326,164.667963 " +
        "C191.183787,168.842556 183.854737,172.420929 176.223542,175.320965 C180.230393,183.341335 184.861538,190.991831 190.096624,198.16893 " +
        "C211.238746,191.588051 232.743023,181.531619 254.911949,164.955721 C260.227747,108.668201 245.831087,59.8662432 216.856339,16.5966031 Z " +
        "M85.4738752,135.09489 C72.8290281,135.09489 62.4592217,123.290155 62.4592217,108.914901 C62.4592217,94.5396472 72.607595,82.7145587 85.4738752,82.7145587 " +
        "C98.3405064,82.7145587 108.709962,94.5189427 108.488529,108.914901 C108.508531,123.290155 98.3405064,135.09489 85.4738752,135.09489 Z " +
        "M170.525237,135.09489 C157.88039,135.09489 147.510584,123.290155 147.510584,108.914901 C147.510584,94.5396472 157.658606,82.7145587 170.525237,82.7145587 " +
        "C183.391518,82.7145587 193.761324,94.5189427 193.539891,108.914901 C193.539891,123.290155 183.391518,135.09489 170.525237,135.09489 Z";

    // telegram-svgrepo-com.svg (viewBox 0 0 24 24), the visible paper-plane path
    const string Telegram =
        "M19.7773,4.42984 C20.8652,3.97177 22.0315,4.8917 21.8394,6.05639 L19.5705,19.8131 C19.3517,21.1395 17.8949,21.9006 16.678,21.2396 " +
        "C15.6597,20.6865 14.1489,19.8352 12.7873,18.9455 C12.1074,18.5012 10.0255,17.0766 10.2814,16.0625 C10.5002,15.1954 14.0001,11.9375 16.0001,10 " +
        "C16.7857,9.23893 16.4279,8.79926 15.5001,9.5 C13.1985,11.2383 9.50332,13.8812 8.28136,14.625 C7.20323,15.2812 6.64031,15.3932 5.96886,15.2812 " +
        "C4.74273,15.0769 3.60596,14.7605 2.67788,14.3758 C1.42351,13.8558 1.48461,12.132 2.67703,11.63 L19.7773,4.42984 Z";

    // Each icon's size inside the bubble (fraction of the bubble's diameter) and a small nudge so it looks centred.
    static readonly Dictionary<string, (string Data, float Size, float NudgeX, float NudgeY)> Defs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["discord"]  = (Discord, 0.60f, 0f, 0.01f),
        ["telegram"] = (Telegram, 0.56f, -0.035f, 0.01f),
    };

    static readonly Dictionary<string, GraphicsPath> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool Has(string? name) => !string.IsNullOrEmpty(name) && Defs.ContainsKey(name);

    /// <summary>Fills the named icon, centred in a circle of the given centre and radius.</summary>
    public static void Draw(Graphics g, string name, float cx, float cy, float radius, Brush brush)
    {
        if (!Defs.TryGetValue(name, out var def)) return;
        if (!Cache.TryGetValue(name, out var source))
        {
            source = Parse(def.Data);
            Cache[name] = source;
        }

        var b = source.GetBounds();
        if (b.Width <= 0 || b.Height <= 0) return;

        float box = radius * 2f * def.Size;
        float scale = box / Math.Max(b.Width, b.Height);
        using var path = (GraphicsPath)source.Clone();
        using var m = new Matrix();
        m.Translate(cx + def.NudgeX * radius * 2f, cy + def.NudgeY * radius * 2f);
        m.Scale(scale, scale);
        m.Translate(-(b.Left + b.Width / 2f), -(b.Top + b.Height / 2f));
        path.Transform(m);
        g.FillPath(brush, path);
    }

    /// <summary>Minimal SVG path parser: M L H V C S Q T Z, absolute and relative. Arcs aren't needed here.</summary>
    static GraphicsPath Parse(string d)
    {
        var path = new GraphicsPath(FillMode.Alternate);
        var tokens = Tokenize(d);
        int i = 0;
        char cmd = ' ';
        PointF cur = PointF.Empty, start = PointF.Empty, lastCtrl = PointF.Empty;
        char prev = ' ';

        float Num() => tokens[i++].Num;
        bool NextIsNum() => i < tokens.Count && !tokens[i].IsCmd;

        while (i < tokens.Count)
        {
            if (tokens[i].IsCmd) cmd = tokens[i++].Cmd;
            bool rel = char.IsLower(cmd);
            PointF P(float x, float y) => rel ? new PointF(cur.X + x, cur.Y + y) : new PointF(x, y);

            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    cur = start = P(Num(), Num());
                    path.StartFigure();
                    cmd = rel ? 'l' : 'L';   // further pairs are line-tos
                    break;
                case 'L':
                {
                    var p = P(Num(), Num());
                    path.AddLine(cur, p); cur = p;
                    break;
                }
                case 'H':
                {
                    float x = Num();
                    var p = new PointF(rel ? cur.X + x : x, cur.Y);
                    path.AddLine(cur, p); cur = p;
                    break;
                }
                case 'V':
                {
                    float y = Num();
                    var p = new PointF(cur.X, rel ? cur.Y + y : y);
                    path.AddLine(cur, p); cur = p;
                    break;
                }
                case 'C':
                {
                    var c1 = P(Num(), Num()); var c2 = P(Num(), Num()); var p = P(Num(), Num());
                    path.AddBezier(cur, c1, c2, p); lastCtrl = c2; cur = p;
                    break;
                }
                case 'S':
                {
                    var c1 = "CS".Contains(char.ToUpperInvariant(prev)) ? Reflect(lastCtrl, cur) : cur;
                    var c2 = P(Num(), Num()); var p = P(Num(), Num());
                    path.AddBezier(cur, c1, c2, p); lastCtrl = c2; cur = p;
                    break;
                }
                case 'Q':
                {
                    var q = P(Num(), Num()); var p = P(Num(), Num());
                    AddQuad(path, cur, q, p); lastCtrl = q; cur = p;
                    break;
                }
                case 'T':
                {
                    var q = "QT".Contains(char.ToUpperInvariant(prev)) ? Reflect(lastCtrl, cur) : cur;
                    var p = P(Num(), Num());
                    AddQuad(path, cur, q, p); lastCtrl = q; cur = p;
                    break;
                }
                case 'Z':
                    path.CloseFigure();
                    cur = start;
                    break;
                default:
                    i++;   // unknown token; skip it
                    break;
            }
            prev = cmd;
            if (char.ToUpperInvariant(cmd) == 'Z' && NextIsNum()) cmd = 'L';
        }
        return path;
    }

    static PointF Reflect(PointF ctrl, PointF about) => new(2 * about.X - ctrl.X, 2 * about.Y - ctrl.Y);

    static void AddQuad(GraphicsPath path, PointF p0, PointF q, PointF p)
    {
        var c1 = new PointF(p0.X + 2f / 3f * (q.X - p0.X), p0.Y + 2f / 3f * (q.Y - p0.Y));
        var c2 = new PointF(p.X + 2f / 3f * (q.X - p.X), p.Y + 2f / 3f * (q.Y - p.Y));
        path.AddBezier(p0, c1, c2, p);
    }

    readonly record struct Token(bool IsCmd, char Cmd, float Num);

    static List<Token> Tokenize(string d)
    {
        var list = new List<Token>();
        int i = 0;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (char.IsLetter(c) && c != 'e' && c != 'E') { list.Add(new Token(true, c, 0)); i++; continue; }

            int s = i;
            if (d[i] == '-' || d[i] == '+') i++;
            bool dot = false;
            while (i < d.Length && (char.IsDigit(d[i]) || (d[i] == '.' && !dot)))
            {
                if (d[i] == '.') dot = true;
                i++;
            }
            if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
            {
                i++;
                if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
                while (i < d.Length && char.IsDigit(d[i])) i++;
            }
            if (i == s) { i++; continue; }
            list.Add(new Token(false, ' ', float.Parse(d.AsSpan(s, i - s), NumberStyles.Float, CultureInfo.InvariantCulture)));
        }
        return list;
    }
}
