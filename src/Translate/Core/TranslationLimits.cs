namespace Translate.Core;
internal static class TranslationLimits
{
    internal const int InputCharacters=20_000;
    internal const int OutputCharacters=200_000;
    internal const int EventCharacters=256*1024;
    internal const long ResponseBytes=8*1024*1024;
    internal static void ValidateInput(string text)
    {
        if(string.IsNullOrWhiteSpace(text))throw new ArgumentException("请先输入或选中中文或英文。");
        if(text.Length>InputCharacters)throw new ArgumentException("原文过长，请缩短到 20000 字符以内后重试。");
    }
}
