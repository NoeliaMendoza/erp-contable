using System.Xml.Linq;

namespace ERP.ArchitectureTests;

/// <summary>
/// Límites de módulo. Si una prueba falla, no la "arregle" relajándola: discuta el cambio en un PR con ADR.
/// </summary>
public sealed class DependencyRulesTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly Dictionary<string, Project> Projects = Directory
        .EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
        .Select(Project.Load)
        .ToDictionary(p => p.Name, StringComparer.Ordinal);

    public static TheoryData<string> ModuleImplementations => [.. Projects.Keys
        .Where(n => n.StartsWith("ERP.Modules.", StringComparison.Ordinal) && !n.EndsWith(".Contracts", StringComparison.Ordinal))];

    public static TheoryData<string> ContractProjects => [.. Projects.Keys.Where(n => n.EndsWith(".Contracts", StringComparison.Ordinal))];

    public static TheoryData<string> FrontendProjects => [.. Projects.Keys.Where(n => n.StartsWith("ERP.Frontend", StringComparison.Ordinal))];

    [Theory]
    [MemberData(nameof(ModuleImplementations))]
    public void Un_modulo_no_referencia_la_implementacion_de_otro_modulo(string module)
    {
        var forbidden = Projects[module].ProjectReferences
            .Where(r => r.StartsWith("ERP.Modules.", StringComparison.Ordinal) && !r.EndsWith(".Contracts", StringComparison.Ordinal))
            .ToList();

        Assert.True(forbidden.Count == 0, $"{module} referencia implementaciones de otros módulos: {string.Join(", ", forbidden)}. Use un proyecto *.Contracts.");
    }

    [Theory]
    [MemberData(nameof(ContractProjects))]
    public void Los_contratos_solo_dependen_de_BuildingBlocks(string contracts)
    {
        var project = Projects[contracts];

        Assert.All(project.ProjectReferences, r => Assert.Equal("ERP.BuildingBlocks", r));
        Assert.Empty(project.PackageReferences);
        Assert.Empty(project.FrameworkReferences);
    }

    [Fact]
    public void BuildingBlocks_es_pequeno_y_sin_dependencias()
    {
        var project = Projects["ERP.BuildingBlocks"];

        Assert.Empty(project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
        Assert.Empty(project.FrameworkReferences);
    }

    [Fact]
    public void Localizacion_Ecuador_no_depende_de_modulos_ni_de_persistencia()
    {
        var project = Projects["ERP.Tributario"];

        Assert.Equal(["ERP.BuildingBlocks"], project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
    }

    [Fact]
    public void Infrastructure_no_depende_de_modulos()
    {
        Assert.DoesNotContain(Projects["ERP.Infrastructure"].ProjectReferences, r => r.StartsWith("ERP.Modules.", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(FrontendProjects))]
    public void El_escritorio_no_accede_al_backend_ni_a_la_base_de_datos(string desktop)
    {
        var project = Projects[desktop];

        Assert.DoesNotContain(project.ProjectReferences, r => r.StartsWith("ERP.Modules.", StringComparison.Ordinal)
            || r is "ERP.Infrastructure" or "ERP.Api" or "ERP.DbMigrator");
        Assert.DoesNotContain(project.PackageReferences, p => p.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Npgsql", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Solo_la_API_y_DbMigrator_componen_todos_los_modulos()
    {
        var composers = Projects.Values
            .Where(p => p.ProjectReferences.Count(r => r.StartsWith("ERP.Modules.", StringComparison.Ordinal) && !r.EndsWith(".Contracts", StringComparison.Ordinal)) > 1)
            .Select(p => p.Name)
            .Order()
            .ToList();

        Assert.Equal(["ERP.Api", "ERP.DbMigrator"], composers);
    }

    [Fact]
    public void Ningun_appsettings_versionado_contiene_contrasenas()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("Password=", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Las_vistas_usan_el_tema_comun_y_no_colores_ni_tamanos_sueltos()
    {
        var hardcoded = new System.Text.RegularExpressions.Regex(
            "(Foreground|Background|BorderBrush|Color)=\"(#|White|Black|Red|Blue|Green)|FontSize=\"[0-9]");

        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "Frontend"), "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("ErpTheme.axaml", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(l => hardcoded.IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}")
            .ToList();

        Assert.True(offenders.Count == 0, "Use recursos/clases de ErpTheme.axaml en lugar de valores fijos: " + string.Join(", ", offenders));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ERP.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró ERP.slnx");
    }

    private sealed record Project(string Name, string[] ProjectReferences, string[] PackageReferences, string[] FrameworkReferences)
    {
        public static Project Load(string path)
        {
            var xml = XDocument.Load(path);
            string[] Includes(string element) => [.. xml.Descendants(element)
                .Select(e => (string?)e.Attribute("Include"))
                .OfType<string>()];

            return new Project(
                Path.GetFileNameWithoutExtension(path),
                [.. Includes("ProjectReference").Select(r => Path.GetFileNameWithoutExtension(r.Replace('\\', '/')))],
                Includes("PackageReference"),
                Includes("FrameworkReference"));
        }
    }
}
