using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ExcelDataTool.Tests.Editor
{
    /// <summary>定位测试目录自带的固定工作簿，使工具移动目录或单独迁移后仍可运行测试。</summary>
    internal static class TestWorkbooks
    {
        /// <summary>从辅助脚本实际编译位置查找相邻 Fixtures 目录，不依赖插件安装路径和用户示例。</summary>
        /// <param name="fileName">夹具文件名，例如 Enemy.xlsx，不允许包含目录部分。</param>
        /// <returns>已存在的固定工作簿绝对路径，调用方必须复制后再修改。</returns>
        /// <exception cref="ArgumentException">传入空文件名或包含路径段。</exception>
        /// <exception cref="FileNotFoundException">没有随测试目录一起迁移对应夹具。</exception>
        internal static string GetPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
                throw new ArgumentException("测试夹具只接受文件名。", nameof(fileName));
            string compiledSource = GetCompiledSourcePath();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string helperPath = Path.GetFullPath(Path.IsPathRooted(compiledSource)
                ? compiledSource : Path.Combine(projectRoot, compiledSource));
            string fixture = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(helperPath), "../Fixtures", fileName));
            if (!File.Exists(fixture))
                throw new FileNotFoundException("缺少固定测试工作簿，请将 Tests/Fixtures 与 Tests/Editor 一起迁移。", fixture);
            return fixture;
        }

        /// <summary>让编译器记录本辅助脚本的位置，兼容 Unity 传入绝对或工程相对源代码路径。</summary>
        private static string GetCompiledSourcePath([CallerFilePath] string sourcePath = "")
        {
            return sourcePath;
        }
    }
}
