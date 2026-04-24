using System.Diagnostics;
using System.Text.RegularExpressions;

var command = args.FirstOrDefault();
if (string.IsNullOrWhiteSpace(command))
{
    PrintUsage();
    return 1;
}

var repositoryRoot = ResolveRepositoryRoot();

return command switch
{
    "validate" => ValidateSpecs(repositoryRoot),
    "lint" => LintSpecs(repositoryRoot),
    "diff" => DiffSpec(repositoryRoot, args.Skip(1).FirstOrDefault()),
    _ => InvalidCommand(command),
};

static int ValidateSpecs(string repositoryRoot)
{
    var specPaths = GetSpecPaths(repositoryRoot);
    var failed = false;

    foreach (var specPath in specPaths.Values)
    {
        var content = File.ReadAllText(specPath);

        if (!content.Contains("openapi: 3.1.0", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"[validate] {specPath}: ожидается openapi: 3.1.0");
            failed = true;
        }

        foreach (var reference in ExtractRefs(content))
        {
            if (!reference.StartsWith("./", StringComparison.Ordinal))
            {
                continue;
            }

            var refPath = reference.Split('#')[0];
            var absoluteRefPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(specPath)!, refPath));

            if (!File.Exists(absoluteRefPath))
            {
                Console.Error.WriteLine($"[validate] {specPath}: не найден $ref файл {refPath}");
                failed = true;
            }
        }
    }

    if (!failed)
    {
        Console.WriteLine("[validate] OpenAPI 3.1 проверки пройдены.");
    }

    return failed ? 1 : 0;
}

static int LintSpecs(string repositoryRoot)
{
    var specPaths = GetSpecPaths(repositoryRoot);
    var failed = false;

    foreach (var (name, path) in specPaths)
    {
        var content = File.ReadAllText(path);
        var operations = ExtractOperations(content);

        foreach (var operation in operations)
        {
            if (!operation.HasOperationId)
            {
                Console.Error.WriteLine($"[lint] {name}: отсутствует operationId у {operation.Method.ToUpperInvariant()} {operation.Path}");
                failed = true;
            }

            if (!operation.HasDefaultResponse)
            {
                Console.Error.WriteLine($"[lint] {name}: отсутствует default response у {operation.Method.ToUpperInvariant()} {operation.Path}");
                failed = true;
            }
        }

        if (name == "internal" && !content.Contains("- internalServiceToken: []", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("[lint] internal: отсутствует обязательный security requirement internalServiceToken");
            failed = true;
        }

        if (name == "worker" && !content.Contains("- workerServiceToken: []", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("[lint] worker: отсутствует обязательный security requirement workerServiceToken");
            failed = true;
        }
    }

    if (!failed)
    {
        Console.WriteLine("[lint] Стайл-проверки OpenAPI пройдены.");
    }

    return failed ? 1 : 0;
}

static int DiffSpec(string repositoryRoot, string? scope)
{
    var specMap = GetSpecPaths(repositoryRoot);
    if (scope is not ("external" or "worker"))
    {
        Console.Error.WriteLine("[diff] Укажите область: external или worker.");
        return 1;
    }

    var relativePath = scope == "external"
        ? "docs/api-contracts/openapi-external.yaml"
        : "docs/api-contracts/openapi-worker.yaml";

    var baseline = TryReadFromGit(repositoryRoot, $"origin/main:{relativePath}")
                   ?? TryReadFromGit(repositoryRoot, $"HEAD~1:{relativePath}");

    if (baseline is null)
    {
        Console.WriteLine($"[diff] baseline для {scope} не найден, проверка пропущена.");
        return 0;
    }

    var current = File.ReadAllText(specMap[scope]);
    var baselineOps = ExtractOperations(baseline).Select(x => $"{x.Method}:{x.Path}").ToHashSet(StringComparer.OrdinalIgnoreCase);
    var currentOps = ExtractOperations(current).Select(x => $"{x.Method}:{x.Path}").ToHashSet(StringComparer.OrdinalIgnoreCase);

    var removedOps = baselineOps.Except(currentOps).OrderBy(x => x).ToList();
    if (removedOps.Count > 0)
    {
        Console.Error.WriteLine("[diff] обнаружены потенциальные breaking changes (удаленные операции):");
        foreach (var removed in removedOps)
        {
            Console.Error.WriteLine($"  - {removed}");
        }

        return 1;
    }

    Console.WriteLine($"[diff] Потенциальных breaking changes для {scope} не обнаружено.");
    return 0;
}

static IEnumerable<string> ExtractRefs(string content)
{
    var regex = new Regex("\\$ref:\\s*['\"](?<ref>[^'\"]+)['\"]", RegexOptions.Compiled);
    return regex.Matches(content).Select(match => match.Groups["ref"].Value);
}

static IReadOnlyCollection<OperationMeta> ExtractOperations(string yaml)
{
    var pathRegex = new Regex("^\\s{2}(?<path>/[^:]+):\\s*$", RegexOptions.Compiled);
    var methodRegex = new Regex("^\\s{4}(?<method>get|post|put|patch|delete|options|head):\\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    var operationIdRegex = new Regex("^\\s{6}operationId:\\s*(?<id>\\S+)", RegexOptions.Compiled);
    var defaultResponseRegex = new Regex("^\\s{8}default:\\s*$", RegexOptions.Compiled);

    var operations = new List<OperationMeta>();
    var currentPath = string.Empty;
    OperationMetaBuilder? currentOperation = null;
    var insideResponses = false;

    foreach (var rawLine in yaml.Split('\n'))
    {
        var line = rawLine.TrimEnd('\r');

        var pathMatch = pathRegex.Match(line);
        if (pathMatch.Success)
        {
            if (currentOperation is not null)
            {
                operations.Add(currentOperation.Build());
                currentOperation = null;
                insideResponses = false;
            }

            currentPath = pathMatch.Groups["path"].Value;
            continue;
        }

        var methodMatch = methodRegex.Match(line);
        if (methodMatch.Success)
        {
            if (currentOperation is not null)
            {
                operations.Add(currentOperation.Build());
            }

            currentOperation = new OperationMetaBuilder(currentPath, methodMatch.Groups["method"].Value.ToLowerInvariant());
            insideResponses = false;
            continue;
        }

        if (currentOperation is null)
        {
            continue;
        }

        if (operationIdRegex.IsMatch(line))
        {
            currentOperation.HasOperationId = true;
            continue;
        }

        if (line.StartsWith("      responses:", StringComparison.Ordinal))
        {
            insideResponses = true;
            continue;
        }

        if (insideResponses && line.StartsWith("      ", StringComparison.Ordinal) && !line.StartsWith("        ", StringComparison.Ordinal))
        {
            insideResponses = false;
        }

        if (insideResponses && defaultResponseRegex.IsMatch(line))
        {
            currentOperation.HasDefaultResponse = true;
        }
    }

    if (currentOperation is not null)
    {
        operations.Add(currentOperation.Build());
    }

    return operations;
}

static Dictionary<string, string> GetSpecPaths(string root)
{
    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["external"] = Path.Combine(root, "docs", "api-contracts", "openapi-external.yaml"),
        ["internal"] = Path.Combine(root, "docs", "api-contracts", "openapi-internal.yaml"),
        ["worker"] = Path.Combine(root, "docs", "api-contracts", "openapi-worker.yaml"),
        ["common"] = Path.Combine(root, "docs", "api-contracts", "openapi-common.yaml"),
    };
}

static string? TryReadFromGit(string repositoryRoot, string revisionSpec)
{
    var psi = new ProcessStartInfo
    {
        FileName = "git",
        Arguments = $"show {revisionSpec}",
        WorkingDirectory = repositoryRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };

    using var process = Process.Start(psi);
    if (process is null)
    {
        return null;
    }

    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();

    return process.ExitCode == 0 ? output : null;
}

static string ResolveRepositoryRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Не удалось определить корень репозитория.");
}

static int InvalidCommand(string command)
{
    Console.Error.WriteLine($"Неизвестная команда: {command}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/DDCRM.Contracts.Cli -- validate");
    Console.WriteLine("  dotnet run --project tools/DDCRM.Contracts.Cli -- lint");
    Console.WriteLine("  dotnet run --project tools/DDCRM.Contracts.Cli -- diff external|worker");
}

sealed record OperationMeta(string Path, string Method, bool HasOperationId, bool HasDefaultResponse);

sealed class OperationMetaBuilder(string path, string method)
{
    public bool HasOperationId { get; set; }

    public bool HasDefaultResponse { get; set; }

    public OperationMeta Build() => new(path, method, HasOperationId, HasDefaultResponse);
}
