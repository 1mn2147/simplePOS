using System.Xml.Linq;

namespace Pos.Desktop.Services;

public sealed record MacLaunchCommand(
    string Program,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null)
{
    public static MacLaunchCommand ForCurrentProcess(IEnumerable<string>? applicationArguments = null)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("현재 실행 파일 경로를 확인할 수 없습니다.");
        var arguments = applicationArguments?.ToArray() ?? [];

        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssembly = Environment.GetCommandLineArgs().FirstOrDefault();
            if (string.IsNullOrWhiteSpace(entryAssembly) || !File.Exists(entryAssembly))
            {
                throw new InvalidOperationException("개발 실행에 사용할 애플리케이션 어셈블리를 확인할 수 없습니다.");
            }

            return new MacLaunchCommand(processPath, [Path.GetFullPath(entryAssembly), .. arguments], ReadEnvironment());
        }

        return new MacLaunchCommand(processPath, arguments, ReadEnvironment());
    }

    private static IReadOnlyDictionary<string, string>? ReadEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        var simplePosHome = Environment.GetEnvironmentVariable("SIMPLEPOS_HOME");
        if (!string.IsNullOrWhiteSpace(simplePosHome))
        {
            environment["SIMPLEPOS_HOME"] = Path.GetFullPath(simplePosHome);
        }

        var dotnetRootVariable = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                                 System.Runtime.InteropServices.Architecture.Arm64
            ? "DOTNET_ROOT_ARM64"
            : "DOTNET_ROOT";
        var dotnetRoot = Environment.GetEnvironmentVariable(dotnetRootVariable)
            ?? Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot) && Path.IsPathFullyQualified(dotnetRoot))
        {
            environment[dotnetRootVariable] = Path.GetFullPath(dotnetRoot);
        }
        else if (IsDevelopmentAppHost(Environment.ProcessPath))
        {
            var runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            var resolvedRoot = Directory.GetParent(Path.TrimEndingDirectorySeparator(runtimeDirectory))?.Parent?.Parent?.FullName;
            if (!string.IsNullOrWhiteSpace(resolvedRoot))
            {
                environment[dotnetRootVariable] = resolvedRoot;
            }
        }

        return environment.Count == 0 ? null : environment;
    }

    private static bool IsDevelopmentAppHost(string? processPath) =>
        !string.IsNullOrWhiteSpace(processPath) &&
        processPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
        File.Exists(processPath + ".runtimeconfig.json");
}

public sealed class MacLoginItemService
{
    private readonly string _label;

    public MacLoginItemService(
        string label = "com.simplepos.desktop",
        string? launchAgentsDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        EnsureMacOS();

        if (label.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
        {
            throw new ArgumentException("로그인 항목 레이블에는 영문자, 숫자, 점, 하이픈만 사용할 수 있습니다.", nameof(label));
        }

        _label = label;
        var directory = launchAgentsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents");
        PlistPath = Path.Combine(directory, $"{label}.plist");
    }

    public string PlistPath { get; }

    public bool IsRegistered(MacLaunchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!File.Exists(PlistPath))
        {
            return false;
        }

        try
        {
            return CommandsEqual(ReadCommand(PlistPath), command);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          System.Xml.XmlException or InvalidDataException)
        {
            return false;
        }
    }

    public void SetRegistered(bool registered, MacLaunchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!registered)
        {
            File.Delete(PlistPath);
            return;
        }

        ValidateCommand(command);
        var directory = Path.GetDirectoryName(PlistPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{_label}.{Guid.NewGuid():N}.tmp.plist");

        try
        {
            CreateDocument(command).Save(temporaryPath, SaveOptions.DisableFormatting);
            if (!CommandsEqual(ReadCommand(temporaryPath), command))
            {
                throw new InvalidDataException("생성한 로그인 항목을 다시 읽어 검증하지 못했습니다.");
            }

            File.Move(temporaryPath, PlistPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private XDocument CreateDocument(MacLaunchCommand command) => new(
        new XDeclaration("1.0", "UTF-8", null),
        new XDocumentType(
            "plist",
            "-//Apple//DTD PLIST 1.0//EN",
            "http://www.apple.com/DTDs/PropertyList-1.0.dtd",
            null),
        new XElement("plist",
            new XAttribute("version", "1.0"),
            new XElement("dict",
                new XElement("key", "Label"),
                new XElement("string", _label),
                new XElement("key", "ProgramArguments"),
                new XElement("array",
                    new XElement("string", command.Program),
                    command.Arguments.Select(argument => new XElement("string", argument))),
                CreateEnvironmentElements(command.EnvironmentVariables),
                new XElement("key", "RunAtLoad"),
                new XElement("true"))));

    private MacLaunchCommand ReadCommand(string path)
    {
        var dictionary = XDocument.Load(path).Root?.Element("dict")
            ?? throw new InvalidDataException("로그인 항목 plist에 dict가 없습니다.");
        var elements = dictionary.Elements().ToArray();
        var label = ReadValue(elements, "Label", "string");
        if (!string.Equals(label, _label, StringComparison.Ordinal))
        {
            throw new InvalidDataException("로그인 항목 레이블이 일치하지 않습니다.");
        }

        var argumentsElement = FindValueElement(elements, "ProgramArguments", "array");
        var arguments = argumentsElement.Elements("string").Select(element => element.Value).ToArray();
        if (arguments.Length == 0)
        {
            throw new InvalidDataException("로그인 항목에 실행 명령이 없습니다.");
        }

        return new MacLaunchCommand(arguments[0], arguments[1..], ReadEnvironment(elements));
    }

    private static object[] CreateEnvironmentElements(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null || environment.Count == 0)
        {
            return [];
        }

        return
        [
            new XElement("key", "EnvironmentVariables"),
            new XElement("dict",
                environment.OrderBy(pair => pair.Key, StringComparer.Ordinal).SelectMany(pair => new object[]
                {
                    new XElement("key", pair.Key),
                    new XElement("string", pair.Value),
                })),
        ];
    }

    private static IReadOnlyDictionary<string, string>? ReadEnvironment(XElement[] elements)
    {
        XElement environment;
        try
        {
            environment = FindValueElement(elements, "EnvironmentVariables", "dict");
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var values = environment.Elements().ToArray();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index += 2)
        {
            if (index + 1 >= values.Length || values[index].Name != "key" || values[index + 1].Name != "string")
            {
                throw new InvalidDataException("로그인 항목 환경 변수 형식이 올바르지 않습니다.");
            }
            result.Add(values[index].Value, values[index + 1].Value);
        }
        return result;
    }

    private static bool CommandsEqual(MacLaunchCommand left, MacLaunchCommand right) =>
        string.Equals(left.Program, right.Program, StringComparison.Ordinal) &&
        left.Arguments.SequenceEqual(right.Arguments, StringComparer.Ordinal) &&
        EnvironmentsEqual(left.EnvironmentVariables, right.EnvironmentVariables);

    private static bool EnvironmentsEqual(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if ((left?.Count ?? 0) != (right?.Count ?? 0)) return false;
        if (left is null || left.Count == 0) return true;
        return right is not null && left.All(pair =>
            right.TryGetValue(pair.Key, out var value) && string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    private static string ReadValue(XElement[] elements, string key, string elementName) =>
        FindValueElement(elements, key, elementName).Value;

    private static XElement FindValueElement(XElement[] elements, string key, string elementName)
    {
        for (var index = 0; index < elements.Length - 1; index++)
        {
            if (elements[index].Name == "key" && elements[index].Value == key &&
                elements[index + 1].Name == elementName)
            {
                return elements[index + 1];
            }
        }

        throw new InvalidDataException($"로그인 항목에 {key} 값이 없습니다.");
    }

    private static void ValidateCommand(MacLaunchCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Program);
        if (!Path.IsPathFullyQualified(command.Program) || !File.Exists(command.Program))
        {
            throw new FileNotFoundException("로그인 시 실행할 프로그램을 찾을 수 없습니다.", command.Program);
        }

        if (command.Arguments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("로그인 실행 인수는 비어 있을 수 없습니다.", nameof(command));
        }

        if (command.EnvironmentVariables?.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) == true)
        {
            throw new ArgumentException("로그인 환경 변수의 이름과 값은 비어 있을 수 없습니다.", nameof(command));
        }
    }

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("로그인 항목 등록은 macOS에서만 지원됩니다.");
        }
    }
}
