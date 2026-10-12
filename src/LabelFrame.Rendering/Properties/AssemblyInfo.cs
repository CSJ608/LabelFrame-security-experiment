using System.Runtime.CompilerServices;

// 字型回退单测需直查内部查找器 SkiaTypefaceLookup（WinHost.Tests 为其唯一测试消费方）
[assembly: InternalsVisibleTo("LabelFrame.WinHost.Tests")]
