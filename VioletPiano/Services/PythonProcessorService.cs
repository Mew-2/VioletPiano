// Services/PythonProcessorService.cs
using System.Diagnostics;
using System.Text;

public class PythonProcessorService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<PythonProcessorService> _logger;

    public PythonProcessorService(IWebHostEnvironment environment, ILogger<PythonProcessorService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<(bool success, string message, string outputPath)> ProcessAudioAsync(Stream audioStream, string fileName)
    {
        var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads");
        var outputsPath = Path.Combine(_environment.WebRootPath, "outputs");

        // 确保目录存在
        Directory.CreateDirectory(uploadsPath);
        Directory.CreateDirectory(outputsPath);

        // 保存上传的音频文件
        var inputPath = Path.Combine(uploadsPath, Guid.NewGuid().ToString() + Path.GetExtension(fileName));
        var outputPath = Path.Combine(outputsPath, Guid.NewGuid().ToString() + ".mid");

        try
        {
            // 保存上传的文件
            using (var fileStream = new FileStream(inputPath, FileMode.Create))
            {
                await audioStream.CopyToAsync(fileStream);
            }

            _logger.LogInformation($"文件保存到: {inputPath}");

            // 转换为WSL路径
            var wslInputPath = ConvertToWslPath(inputPath);
            var wslOutputPath = ConvertToWslPath(outputPath);

            _logger.LogInformation($"WSL输入路径: {wslInputPath}");
            _logger.LogInformation($"WSL输出路径: {wslOutputPath}");

            // 准备WSL命令 - 简化命令结构
            var pythonScriptPath = "/home/zzdw/pop2piano.py";

            // 使用更简单的命令结构
            var command = $"source ~/p2p_env/bin/activate && python {pythonScriptPath} --input \"{wslInputPath}\" --output \"{wslOutputPath}\"";

            _logger.LogInformation($"执行命令: {command}");

            // 执行WSL命令
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = $"-e /bin/bash -c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            using var process = new Process();
            process.StartInfo = processStartInfo;

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    _logger.LogInformation($"Python输出: {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    _logger.LogError($"Python错误: {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 等待处理完成（设置超时时间）
            var timeout = TimeSpan.FromMinutes(5);
            if (await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds)))
            {
                var output = outputBuilder.ToString();
                var error = errorBuilder.ToString();

                _logger.LogInformation($"进程退出代码: {process.ExitCode}");
                _logger.LogInformation($"完整输出: {output}");
                _logger.LogInformation($"完整错误: {error}");

                if (process.ExitCode == 0 && output.Contains("SUCCESS:"))
                {
                    // 检查输出文件是否存在
                    if (File.Exists(outputPath))
                    {
                        return (true, "处理完成", outputPath);
                    }
                    else
                    {
                        return (false, "处理完成但未生成输出文件", null);
                    }
                }
                else if (output.Contains("ERROR:"))
                {
                    var errorMsg = output.Split("ERROR:")[1].Trim();
                    return (false, $"处理错误: {errorMsg}", null);
                }
                else
                {
                    return (false, $"处理失败，退出代码: {process.ExitCode}", null);
                }
            }
            else
            {
                process.Kill();
                return (false, "处理超时", null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理音频时发生异常");
            return (false, $"系统错误: {ex.Message}", null);
        }
        finally
        {
            // 清理输入文件
            if (File.Exists(inputPath))
            {
                try { File.Delete(inputPath); } catch { }
            }
        }
    }

    private string ConvertToWslPath(string windowsPath)
    {
        try
        {
            // 规范化Windows路径
            windowsPath = Path.GetFullPath(windowsPath);

            // 获取驱动器号和剩余路径
            var drive = Path.GetPathRoot(windowsPath);
            if (string.IsNullOrEmpty(drive) || drive.Length < 2)
            {
                throw new ArgumentException("无效的Windows路径");
            }

            // 提取驱动器字母（去掉冒号）
            var driveLetter = drive[0].ToString().ToLower();

            // 获取剩余路径
            var relativePath = windowsPath.Substring(drive.Length);

            // 转换为WSL路径格式
            var wslPath = $"/mnt/{driveLetter}/{relativePath.Replace("\\", "/").TrimStart('/')}";

            _logger.LogInformation($"路径转换: {windowsPath} -> {wslPath}");

            return wslPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "路径转换失败");
            throw;
        }
    }

    // 可选：验证WSL路径是否存在的方法
    private async Task<bool> ValidateWslPath(string wslPath)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = $"-e test -f \"{wslPath}\" && echo \"EXISTS\" || echo \"NOT_EXISTS\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process();
            process.StartInfo = processStartInfo;

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            return output.Contains("EXISTS");
        }
        catch
        {
            return false;
        }
    }
}