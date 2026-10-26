using LabelFrame.Core.Jobs;
using System.Text;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

if (args.Length != 3 || args[0] is not ("to-split" or "to-legacy") || args[2] != "--hosts-stopped")
{
    Console.Error.WriteLine("用法：LabelFrame.JobStorageTool <to-split|to-legacy> <作业库路径> --hosts-stopped");
    Console.Error.WriteLine("必须先停止所有使用此作业库的宿主。仅用于本地作业库，不用于 ServerDb、模板库或日志库。");
    return 2;
}

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancelHandler;
try
{
    var backup = await JobStorageMigration.ConvertAsync(args[1], args[0] == "to-split", cancellationToken: cancellation.Token);
    Console.WriteLine(backup is null ? "库格式已经匹配，未转换。" : "转换成功，原数据一致性备份（已验证）：" + backup);
    Console.WriteLine("禁止用旧备份覆盖含新作业的当前库。降级程序仅可在 to-legacy 成功后进行。");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("转换失败：" + exception.Message);
    if (exception.Data["JobStorageBackupPath"] is string backup)
    {
        Console.Error.WriteLine("备份路径：" + backup);
        Console.Error.WriteLine(exception.Data["JobStorageBackupVerified"] is true
            ? "备份已验证；工具未自动恢复备份。"
            : "备份未验证，不能视为可恢复副本。请保留文件供诊断。");
    }
    Console.Error.WriteLine("请保持宿主停止，检查库格式及故障原因；不要直接安装旧版或恢复陈旧备份。");
    return exception is OperationCanceledException ? 130 : 1;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
