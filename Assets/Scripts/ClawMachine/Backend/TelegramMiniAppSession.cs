using System;
using UnityEngine;
using UnityEngine.Networking;

public static class TelegramMiniAppSession
{
    [Serializable]
    private sealed class TelegramUserDto
    {
        public long id = 0;
        public string username = string.Empty;
        public string first_name = string.Empty;
        public string last_name = string.Empty;
        public string language_code = string.Empty;
    }

    public static event Action SessionChanged;

    public static bool IsMiniApp { get; private set; }
    public static long UserId { get; private set; }
    public static string Username { get; private set; } = string.Empty;
    public static string FirstName { get; private set; } = string.Empty;
    public static string LastName { get; private set; } = string.Empty;
    public static string LanguageCode { get; private set; } = string.Empty;
    public static string QueryId { get; private set; } = string.Empty;
    public static string AuthDateUnixSeconds { get; private set; } = string.Empty;
    public static string StartParam { get; private set; } = string.Empty;
    public static string ChatType { get; private set; } = string.Empty;
    public static string Platform { get; private set; } = string.Empty;
    public static string InitDataSource { get; private set; } = string.Empty;

    public static string DisplayName
    {
        get
        {
            var first = (FirstName ?? string.Empty).Trim();
            var last = (LastName ?? string.Empty).Trim();
            var fullName = (first + " " + last).Trim();
            if (!string.IsNullOrWhiteSpace(fullName))
                return fullName;

            var username = (Username ?? string.Empty).Trim().TrimStart('@');
            if (!string.IsNullOrWhiteSpace(username))
                return "@" + username;

            if (UserId > 0)
                return "id" + UserId;

            return string.Empty;
        }
    }

    public static void Clear(bool notify = true)
    {
        IsMiniApp = false;
        UserId = 0;
        Username = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        LanguageCode = string.Empty;
        QueryId = string.Empty;
        AuthDateUnixSeconds = string.Empty;
        StartParam = string.Empty;
        ChatType = string.Empty;
        Platform = string.Empty;
        InitDataSource = string.Empty;

        if (notify)
            SessionChanged?.Invoke();
    }

    public static void SetFromInitData(string initData, string source)
    {
        Clear(notify: false);

        var normalized = (initData ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            SessionChanged?.Invoke();
            return;
        }

        IsMiniApp = true;
        InitDataSource = (source ?? string.Empty).Trim();

        var pairs = normalized.Split('&', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < pairs.Length; i++)
        {
            var pair = pairs[i];
            var splitIndex = pair.IndexOf('=');
            var rawKey = splitIndex >= 0 ? pair.Substring(0, splitIndex) : pair;
            var rawValue = splitIndex >= 0 && splitIndex < pair.Length - 1 ? pair.Substring(splitIndex + 1) : string.Empty;

            var key = DecodeUrlValue(rawKey);
            var value = DecodeUrlValue(rawValue);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            switch (key)
            {
                case "query_id":
                    QueryId = value;
                    break;
                case "auth_date":
                    AuthDateUnixSeconds = value;
                    break;
                case "start_param":
                    StartParam = value;
                    break;
                case "chat_type":
                    ChatType = value;
                    break;
                case "user":
                    TryApplyUser(value);
                    break;
            }
        }

        if (TryReadUrlParameter(Application.absoluteURL, "tgWebAppPlatform", out var platform))
            Platform = platform;

        SessionChanged?.Invoke();
    }

    private static void TryApplyUser(string userJson)
    {
        if (string.IsNullOrWhiteSpace(userJson))
            return;

        try
        {
            var user = JsonUtility.FromJson<TelegramUserDto>(userJson);
            if (user == null)
                return;

            if (user.id > 0)
                UserId = user.id;

            Username = (user.username ?? string.Empty).Trim();
            FirstName = (user.first_name ?? string.Empty).Trim();
            LastName = (user.last_name ?? string.Empty).Trim();
            LanguageCode = (user.language_code ?? string.Empty).Trim();
        }
        catch
        {
            // Intentionally ignore malformed user payloads.
        }
    }

    private static bool TryReadUrlParameter(string url, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            return false;

        if (TryReadUrlSectionParameter(url, '?', key, out value))
            return true;
        if (TryReadUrlSectionParameter(url, '#', key, out value))
            return true;

        return false;
    }

    private static bool TryReadUrlSectionParameter(string url, char sectionToken, string key, out string value)
    {
        value = string.Empty;
        var sectionStart = url.IndexOf(sectionToken);
        if (sectionStart < 0 || sectionStart >= url.Length - 1)
            return false;

        var section = url.Substring(sectionStart + 1);
        if (sectionToken == '?')
        {
            var fragmentIndex = section.IndexOf('#');
            if (fragmentIndex >= 0)
                section = section.Substring(0, fragmentIndex);
        }
        else
        {
            var innerQueryIndex = section.IndexOf('?');
            if (innerQueryIndex >= 0 && innerQueryIndex < section.Length - 1)
                section = section.Substring(innerQueryIndex + 1);
        }

        if (!TryReadParameter(section, key, out var rawValue))
            return false;

        value = DecodeUrlValue(rawValue);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadParameter(string section, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(section))
            return false;

        var pairs = section.Split('&', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < pairs.Length; i++)
        {
            var pair = pairs[i];
            var splitIndex = pair.IndexOf('=');
            var rawName = splitIndex >= 0 ? pair.Substring(0, splitIndex) : pair;
            var rawValue = splitIndex >= 0 && splitIndex < pair.Length - 1 ? pair.Substring(splitIndex + 1) : string.Empty;

            var name = DecodeUrlValue(rawName);
            if (!string.Equals(name, key, StringComparison.Ordinal))
                continue;

            value = rawValue;
            return true;
        }

        return false;
    }

    private static string DecodeUrlValue(string value)
    {
        var decoded = value ?? string.Empty;
        for (var pass = 0; pass < 2; pass++)
        {
            var unescaped = UnityWebRequest.UnEscapeURL(decoded);
            if (string.Equals(unescaped, decoded, StringComparison.Ordinal))
                break;
            decoded = unescaped;
        }

        return decoded.Trim();
    }
}
