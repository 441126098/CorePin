using System;
using System.ComponentModel;
using System.Security.Principal;

namespace LoLCorePin.Services;

public static class ErrorText
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static string Describe(Exception exception)
    {
        if (exception is Win32Exception win32)
        {
            if (win32.NativeErrorCode == 5)
                return IsElevated
                    ? "访问被拒绝（Win32 5）。本程序已经以管理员运行；受保护进程、反作弊或进程安全策略仍可能阻止访问，提权无法保证解决。"
                    : "访问被拒绝（Win32 5）。同用户普通进程通常无需管理员；若目标以管理员或其他账户运行，手动以管理员启动本程序可能有帮助。反作弊保护仍可能拒绝访问。";
            if (win32.NativeErrorCode is 87 or 1168 or 6)
                return $"目标可能已退出或 CPU 配置已变化（Win32 {win32.NativeErrorCode}）：{win32.Message}";
            return $"Windows 错误 {win32.NativeErrorCode}：{win32.Message}";
        }
        if (exception is EntryPointNotFoundException or DllNotFoundException)
            return "当前 Windows 不提供所需 API；请使用 Windows 10/11 64 位系统。";
        return exception.Message;
    }
}
