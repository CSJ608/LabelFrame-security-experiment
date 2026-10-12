using System.Runtime.CompilerServices;

// 字型回退单测（WinHost.Tests）需直查 internal 的 SkiaTypefaceLookup，删除本声明即断测试
[assembly: InternalsVisibleTo("LabelFrame.WinHost.Tests")]
