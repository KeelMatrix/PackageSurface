using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class RestoreValidationTests
{
    [Fact]
    public void MalformedAssetsShapesFailClosed()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunMalformedAssetsShapeRegression(workspace.AssetsPath, workspace.Root);
    }

    [Fact]
    public void PackageFolderMetadataIsValidated()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunPackageFoldersValidationRegression(workspace.AssetsPath, workspace.Root);
    }

    [Fact]
    public void AssetsFormatIsValidated()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunAssetsFormatRegression(workspace.AssetsPath, workspace.Root);
    }

    [Fact]
    public void CapabilityGroupsAreValidated()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunCapabilityGroupValidationRegression(workspace.Root);
    }

    [Fact]
    public void FrameworkMonikersAreCanonicalized()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunFrameworkMonikerRegression(workspace.AssetsPath, workspace.Root);
    }

    [Fact]
    public void StructuralDuplicatePropertiesFailClosed()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunRestoreJsonStructuralDuplicateRegression(workspace.AssetsPath, workspace.Root);
    }

    [Fact]
    public void OptionalRestoreMetadataIsValidated()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunRestoreOptionalMetadataRegression(workspace.AssetsPath, workspace.Root);
    }

    [Fact]
    public void RestoreMetadataShapesFailClosed()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunRestoreMetadataShapeRegression(workspace.AssetsPath, workspace.Root);
    }
}
