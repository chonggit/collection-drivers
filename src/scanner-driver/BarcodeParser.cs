using System.Text;
using System.Text.RegularExpressions;
using scanner.driver.models;

namespace scanner.driver;

public class BarcodeParser
{
    private readonly ProtocolConfig _protocol;

    public BarcodeParser(ProtocolConfig protocol)
    {
        _protocol = protocol;
    }

    public string? Parse(byte[] raw)
    {
        if (raw == null) return null;

        string text = _protocol.ResponseEncoding switch
        {
            "utf8" => Encoding.UTF8.GetString(raw),
            "hex" => BitConverter.ToString(raw).Replace("-", ""),
            _ => Encoding.ASCII.GetString(raw)
        };

        text = text.Trim('\r', '\n', '\0');

        if (!string.IsNullOrEmpty(_protocol.BarcodeRegex))
        {
            var match = Regex.Match(text, _protocol.BarcodeRegex);
            if (!match.Success) return null;
            var group = match.Groups[_protocol.RegexGroupIndex];
            if (!group.Success) return null;
            text = group.Value.Trim();
        }

        if (_protocol.RemovePrefixes != null)
            foreach (var p in _protocol.RemovePrefixes)
                if (text.StartsWith(p))
                    text = text.Substring(p.Length);

        if (_protocol.RemoveSuffixes != null)
            foreach (var s in _protocol.RemoveSuffixes)
                if (text.EndsWith(s))
                    text = text.Substring(0, text.Length - s.Length);

        return string.IsNullOrEmpty(text) ? null : text;
    }
}
