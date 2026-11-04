# Scenario and assertion coverage

The CLI and Probe suites use standard xUnit discovery. Every scenario below has assertions in its owning test method. Reusable fixture builders construct data only; shared assertion utilities apply the same fail-closed and privacy contract to related input cases. No discovered test forwards an unrelated scenario family to a regression runner.

The migration baseline is commit `2f195fe07f3a7b45ab2d2a80f6f29b400281dde6`, with 52 CLI and 21 Probe cases. The inventory below identifies every original CLI and Probe assertion site by its source method and line, and maps it to its current discovered test or shared assertion contract. The immutable source links include the original matrix data and loop combinations. Parameterized cases retain their original labels; both restore formats and every filesystem mode/path combination have separate discovered rows.

## Policy ownership

| Policy | Implementation | Focused tests |
| --- | --- | --- |
| Bounded restore document reading | `RestoreMetadataReader` | `ProbeModuleTests`, `ProbeAnalysisTests` |
| JSON names, SDK shapes, required/optional metadata, formats 3/4 | `RestoreJsonSchema` | `RestoreSchemaTests`, `RestoreStructureTests`, `RestoreMetadataTests` |
| Framework, target, package and root identity | `RestoreIdentityNormalization.RestoreIdentityIndex` | `FrameworkIdentityTests`, `RestoreIdentitySetTests`, `RestorePolicyTests` |
| Version grammar and matching | `DependencyRequirements` | `DependencyRequirementTests`, `PackageVersionTests`, `RestorePolicyTests` |
| Agreement among restore declarations, targets, libraries and inventories | `RestoreConsistencyValidator` | `RestoreConsistencyTests`, `RestoreIdentitySetTests` |
| Generated-import discovery and provenance | `GeneratedImportDiscovery` | `GeneratedImportProvenanceTests` |
| Nested imports, cycles, context and work budgets | `NestedImportTraversal` | `NestedImportTests`, `ImportConditionTests`, `ImportBudgetTests` |
| Static condition grammar and truth/unknown propagation | `StaticImportCondition` | `StaticImportConditionTests`, `ImportConditionTests` |
| Filesystem proof for Exists guards | `ImportExistsCondition` | `FilesystemIdentityTests` (32 import/identity rows and 24 baseline rows) |
| MSBuild XML schema/context and primitive facts | `MsBuildXmlSchema`, `PackageMsBuildXml` | `MsBuildContextTests`, `PackageMsBuildXmlTests`, `PackageMsBuildInspectionTests` |
| Dependency roots and bounded graph traversal | `PackageGraphAnalyzer` | `PackageReachabilityTests`, `ProbeModuleTests` |
| Project references and analyzer include/exclude propagation | `AnalyzerPropagation` | `AnalyzerPropagationTests`, `PackageReachabilityTests` |
| Unique target versions and reachable input selection | `ResolvedPackageResolver`, `ReachablePackageInputs` | `PackageReachabilityTests`, `BaselineAliasTests`, `ProbeModuleTests` |
| CLI arguments, selection and execution | `Options`, `ProjectSelection`, `CommandLine` | `CliParsingTests`, `ProjectSelectionTests`, `InvocationBudgetTests` |
| Baseline schema, persistence, aliases and rollback | `BaselineSchema`, `BaselineStore`, `BaselineFileTransaction` | `BaselinePersistenceTests`, `BaselineAliasTests`, `BaselineRollbackTests` |
| Comparison and text/JSON/SARIF output | `DiffEngine`, `Reports`, `ReportWriter` | `ReportsTests`, `BaselineRollbackTests`, `HostileInputTests` |

## Scenario destinations

| Original scenario method | Current discoverable tests |
| --- | --- |
| `RunAnalyzerExclusionRegression` | AnalyzerPropagationTests.AnalyzerExclusionsMatchRestoreRules |
| `RunAncestorCanonicalizationRegression` | FilesystemIdentityTests.AncestorAliasesAreCanonicalized |
| `RunApplicabilityHardeningRegressions` | ImportApplicabilityTests.ApplicabilityAndPresentStateAreConservative |
| `RunAssetsFormatRegression` | RestoreSchemaTests.AssetsFormatIsValidated; RestoreSchemaTests.FormatFourMetadataMustBeComplete |
| `RunBaselineContractRegression` | BaselinePersistenceTests.BaselineWritesRemainAtomicAndFailClosed; ProjectSelectionTests.ProjectSelectionValidatesInputAndMembership |
| `RunBaselineJsonBoundaryRegression` | BaselinePersistenceTests.BaselineJsonBoundariesAreValidated |
| `RunBaselineTransactionRegression` | BaselineAliasTests (assets, hardlinks, symlinks, generated imports and selected project); BaselinePersistenceTests (size boundaries and read-only files); BaselineRollbackTests (report overflow, serialization and sink failure) |
| `RunCapabilityGroupValidationRegression` | RestoreSchemaTests.CapabilityGroupsAreValidated; RestoreSchemaTests.UnsupportedCapabilityGroupsFailClosed |
| `RunDeepGeneratedPropsRegression` | HostileInputTests.GeneratedPropsXmlDepthIsBounded |
| `RunDependencyRequirementRegression` | DependencyRequirementTests.DependencyRequirementsMustResolve; DependencyRequirementTests.EachDependencyRequirementMustResolve |
| `RunDiagnosticPathLeakRegression` | HostileInputTests.DiagnosticsDoNotExposeLocalPaths |
| `RunDtdGeneratedPropsRegression` | HostileInputTests.GeneratedPropsRejectDocumentTypeDeclarations |
| `RunFrameworkMonikerRegression` | FrameworkIdentityTests.FrameworkMonikersAreCanonicalized; FrameworkIdentityTests.FrameworkAliasesMatchResolvedTargets |
| `RunGeneratedImportReachabilityRegression` | GeneratedImportProvenanceTests.GeneratedImportsMustBeReachable |
| `RunIdentityAndReportContractTests` | ReportsTests.IdentityAndReportContracts |
| `RunImportEdgeBudgetRegression` | ImportBudgetTests.ImportGraphWorkIsBounded |
| `RunInlineTaskConventionRegression` | PackageMsBuildXmlTests.InlineTaskConventionIsDetected |
| `RunInvalidAnalyzerMetadataRegression` | HostileInputTests.InvalidCompilerMetadataFailsClosed |
| `RunInvalidMsBuildContextRegression` | MsBuildContextTests.GeneratedImportContextMustBeSupported; NestedImportContextMustBeSupported; PrimitiveContextMustBeSupported; MsBuildContextTests.GeneratedImportContextMustBeSupported; MsBuildContextTests.NestedImportContextMustBeSupported; MsBuildContextTests.PrimitiveContextMustBeSupported |
| `RunInvocationStructuralBudgetRegression` | InvocationBudgetTests.InvocationStructureIsBounded |
| `RunLanguageConventionRegression` | CompilerExtensionTests.CompilerExtensionLanguageConventionsAreApplied |
| `RunLargeGraphBudgetRegression` | HostileInputTests.LargeReachableGraphStaysWithinTheResourceBudget |
| `RunMacOsRootAliasPredicateRegression` | PlatformPathTests.MacosRootAliasPredicateIsStable |
| `RunMalformedAssetsShapeRegression` | RestoreSchemaTests.MalformedAssetsShapesFailClosed; RestoreSchemaTests.MalformedTargetMetadataFailsClosed; RestoreSchemaTests.MalformedDocumentShapesFailClosed |
| `RunMalformedVersionEndToEndRegression` | PackageVersionTests: AllCapabilityVersionFixtureCreatesBaseline, MalformedPackageKeysFailThroughTheCli, MalformedLibraryPathFailsThroughTheCli, MalformedDirectRangeFailsThroughTheCli, MalformedTargetRangeFailsThroughTheCli, MalformedProjectRangeFailsThroughTheCli, MalformedFormatFourRangeFailsThroughTheCli, MalformedGeneratedImportVersionFailsThroughTheCli, MalformedBaselineVersionFailsInEveryReport |
| `RunMixedFilesystemIdentityRegression` | FilesystemIdentityTests.PackageAndProjectIdentityUseTheirOwnFilesystem; FilesystemIdentityTests.BaselineDuplicateIdentityUsesSelectedRoots |
| `RunNestedImportRegression` | NestedImportTests.NestedImportsAreAnalyzed |
| `RunNestedPhaseMatrixRegression` | ImportConditionTests.NestedImportPhasesRemainDistinct; FrameworkLiteralsUseStringSemantics; RightHandPropertyReferenceIsResolved; QuotedBooleanConditionsAreResolved; OuterBuildHasIndependentFrameworkContext; UnsupportedExpansionRemainsUnknown |
| `RunOversizedRestoreRegression` | HostileInputTests.OversizedRestoreInputFailsThroughTheCli |
| `RunPackageFoldersValidationRegression` | RestoreSchemaTests.PackageFolderMetadataIsValidated; RestoreSchemaTests.MalformedPackageFoldersFailClosed |
| `RunPackageIdentityAndPathRegression` | PackageIdentityTests.PackageIdentityAndAssetPathsAreCanonical |
| `RunPackageXmlSchemaRegression` | PackageMsBuildXmlTests.PackageMsbuildXmlMustMatchTheSupportedSchema; PackageMsBuildXmlTests.ConsumedXmlElementSpellingMustBeCanonical; PackageMsBuildXmlTests.ConsumedXmlAttributeSpellingMustBeCanonical |
| `RunParserMessageRegression` | CliParsingTests.ParserErrorsAreStableAndActionable |
| `RunParserTokenPrivacyRegression` | TelemetryTests.ParserTokensAreNeverReported |
| `RunProjectAggregationRegression` | AssetClassificationTests.ProjectLevelAssetsAreAggregated |
| `RunProjectExtensionsRegression` | GeneratedImportProvenanceTests.ProjectExtensionsAreResolved |
| `RunReachabilityClosureRegression` | PackageReachabilityTests.ReachabilityClosureIncludesOnlyResolvedPackages |
| `RunReachablePackageAliasRegression` | BaselineAliasTests.ReachablePackageAliasesAreRejected; FileSymlinkCannotReplacePackageInput; DirectorySymlinkCannotReplacePackageInput; ReachableInputInventoryAllowsDistinctOutput |
| `RunReparsePathVariantRegression` | PackageContainmentTests.ReparsePathVariantsAreRejected |
| `RunReparsePointRegression` | PackageContainmentTests.ReparsePointsAreRejected |
| `RunRestoreIdentityCanonicalizationRegression` | RestoreConsistencyTests.RestoreIdentitiesReconcileAcrossMetadataSources; RestoreConsistencyTests.FrameworkMetadataMustAgree; RestoreConsistencyTests.DuplicateFrameworkIdentitiesFailClosed; RestoreConsistencyTests.MalformedPackageIdentitiesFailClosed |
| `RunRestoreIdentitySetCompletenessRegression` | RestoreIdentitySetTests.FrameworkSetsMustAgree; RestoreIdentitySetTests.EveryIdentitySetMustAgree |
| `RunRestoreJsonStructuralDuplicateRegression` | RestoreStructureTests.StructuralDuplicatePropertiesFailClosed; RestoreStructureTests.DuplicateJsonPropertiesFailClosed; RestoreStructureTests.CaseVariantJsonPropertiesFailClosed; RestoreStructureTests.UnknownJsonPropertiesFailClosed |
| `RunRestoreMetadataShapeRegression` | RestoreMetadataTests.SupportedMetadataShapesRoundTrip; RestoreMetadataTests.MalformedMetadataShapesFailClosed (34 mutations × formats 3/4) |
| `RunRestoreOptionalMetadataRegression` | RestoreMetadataTests.OptionalRestoreMetadataIsValidated |
| `RunStrictContentEligibilityMatrixTests` | ReportsTests.StrictContentEligibilityMatrix |
| `RunTelemetryEligibilityTests` | TelemetryTests.EligibilityDoesNotExposePackageIdentity |
| `RunTerminalIncompleteReportRegression` | BaselinePersistenceTests.IncompleteAnalysisPreservesTheExistingBaseline |
| `RunTraversalAssetPathRegression` | HostileInputTests.TraversalLookingAssetPathsFailClosed |
| `RunVersionAwareReachabilityConflictRegression` | PackageReachabilityTests.VersionConflictsAreResolvedPerTarget; PackageReachabilityTests.TargetVersionsMustMatchDependencyRequirements |
| `RunVersionEquivalenceRegression` | PackageVersionTests.VersionEquivalenceUsesNugetSemantics |
| `RunSymlinkPackageAliasCase` | `BaselineAliasTests.FileSymlinkCannotReplacePackageInput` |
| `RunDirectorySymlinkPackageAliasCase` | `BaselineAliasTests.DirectorySymlinkCannotReplacePackageInput` (`interior`, `ancestor`) |

## Assertion-site inventory

Each row names the original assertion expression's site. Descriptions are the original failure message or assertion call, not a new acceptance rule. Most expressions are preserved verbatim apart from `Require` becoming `Assert.True`. The baseline seed is now captured during fixture construction and asserted through `seedResult.ExitCode`; the two metadata success assertions are theory rows parameterized by format. Those are equivalent assertions of the same command outcomes.

### CliParsingTests.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Cli.Tests/CliParsingTests.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `DiffReportsAllCapabilityChanges:25` | Assert.Equal(ExpectedDiagnosticIds, actual.Order(StringComparer.Ordinal)) | `CliParsingTests.DiffReportsAllCapabilityChanges` |
| `HelpDocumentsTheStableCommandContract:45` | Assert.DoesNotContain(clauses, clause => !normalized.Contains(clause, StringComparison.Ordinal)) | `CliParsingTests.HelpDocumentsTheStableCommandContract` |

### ProbeAnalysisTests.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Probe.Tests/ProbeAnalysisTests.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `ValidEmptyPackageGraphProducesACompleteSurface:21` | ;  | `ProbeAnalysisTests.BuildImportConditionsUseTargetAliasesForEachFramework`, `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface` |
| `ValidEmptyPackageGraphProducesACompleteSurface:22` | Assert.Empty(result.Entries) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `ValidEmptyPackageGraphProducesACompleteSurface:23` | Assert.Equal(1, result.ResolvedPackageCount) | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface` |
| `MalformedRestoreDocumentsFailClosed:37` | Assert.False(result.IsComplete) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.MissingReachablePackageAssetFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `MalformedRestoreDocumentsFailClosed:38` | Assert.Empty(result.Entries) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `MalformedRestoreDocumentsFailClosed:39` | Assert.NotEmpty(result.IncompleteReasons) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed` |
| `MissingAssetsFileFailsClosed:49` | Assert.False(result.IsComplete) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.MissingReachablePackageAssetFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `MissingAssetsFileFailsClosed:50` | Assert.Empty(result.Entries) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `MissingAssetsFileFailsClosed:51` | Assert.NotEmpty(result.IncompleteReasons) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed` |
| `TraversalLookingPackageAssetFailsClosed:61` | Assert.False(result.IsComplete) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.MissingReachablePackageAssetFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `TraversalLookingPackageAssetFailsClosed:62` | Assert.Empty(result.Entries) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `TraversalLookingPackageAssetFailsClosed:63` | invalid relative asset path | `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed` |
| `MissingReachablePackageAssetFailsClosed:73` | Assert.False(result.IsComplete) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.MissingReachablePackageAssetFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `MissingReachablePackageAssetFailsClosed:74` | unsafe package path | `ProbeAnalysisTests.MissingReachablePackageAssetFailsClosed` |
| `OversizedRestoreFileFailsClosed:85` | Assert.False(result.IsComplete) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.MissingReachablePackageAssetFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `OversizedRestoreFileFailsClosed:86` | Assert.Empty(result.Entries) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `OversizedRestoreFileFailsClosed:87` | Assert.NotEmpty(result.IncompleteReasons) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:140` | ;  | `ProbeAnalysisTests.BuildImportConditionsUseTargetAliasesForEachFramework`, `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:141` | Assert.Equal(1, result.ResolvedPackageCount) | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:142` | Assert.Equal(4, result.Entries.Count) | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:143` | direct | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:145` | direct | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:146` | Assert.Equal(CapabilityKind.NativeRuntime, entry.Capability) | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:147` | Assert.True(entry.Present) | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:149` | linux-x64 | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:150` | linux-x64 | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:151` | win-x64 | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState` |
| `ComposedTargetResultsPreserveFrameworkRidAndActiveState:152` | win-x64 | `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState` |
| `BuildImportConditionsUseTargetAliasesForEachFramework:202` | ;  | `ProbeAnalysisTests.BuildImportConditionsUseTargetAliasesForEachFramework`, `ProbeAnalysisTests.ComposedTargetResultsPreserveFrameworkRidAndActiveState`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface` |
| `BuildImportConditionsUseTargetAliasesForEachFramework:204` | Assert.Equal(2, entries.Length) | `ProbeAnalysisTests.BuildImportConditionsUseTargetAliasesForEachFramework` |
| `BuildImportConditionsUseTargetAliasesForEachFramework:205` | {entry.TargetFramework} should match the import target alias. | `ProbeAnalysisTests.BuildImportConditionsUseTargetAliasesForEachFramework` |
| `BuildImportConditionsUseTargetAliasesForEachFramework:205` | {entry.TargetFramework} should match the import target alias. | `ProbeAnalysisTests.BuildImportConditionsUseTargetAliasesForEachFramework` |

### ProbeModuleTests.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Probe.Tests/ProbeModuleTests.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `RestoreMetadataReaderReadsTheProjectIdentity:18` | Test.csproj | `ProbeModuleTests.RestoreMetadataReaderReadsTheProjectIdentity` |
| `PackageGraphReaderReturnsOnlyReachablePackageInputs:28` | fixture.ps1 | `ProbeModuleTests.PackageGraphReaderReturnsOnlyReachablePackageInputs` |
| `GeneratedImportConditionParserEvaluatesTargetFrameworks:40` | Assert.True(parsed.IsKnown, parsed.Reason) | `ProbeModuleTests.GeneratedImportConditionParserEvaluatesTargetFrameworks`, `StaticImportConditionTests.StaticGrammarUsesMsbuildStringAndBooleanSemantics` |
| `GeneratedImportConditionParserEvaluatesTargetFrameworks:41` | net8.0 | `ProbeModuleTests.GeneratedImportConditionParserEvaluatesTargetFrameworks` |
| `GeneratedImportConditionParserEvaluatesTargetFrameworks:42` | net9.0 | `ProbeModuleTests.GeneratedImportConditionParserEvaluatesTargetFrameworks` |
| `AssetClassifierMapsBuildTransitiveFiles:50` | Assert.True(recognized) | `ProbeModuleTests.AssetClassifierMapsBuildTransitiveFiles` |
| `AssetClassifierMapsBuildTransitiveFiles:51` | Assert.Equal(CapabilityKind.BuildTransitive, capability) | `ProbeModuleTests.AssetClassifierMapsBuildTransitiveFiles` |
| `FilesystemSafetyChecksContainmentAtTheModuleSeam:65` | Assert.True(PackageFileSystemSafety.IsWithinDirectory(root, inside, allowRoot: false)) | `ProbeModuleTests.FilesystemSafetyChecksContainmentAtTheModuleSeam` |
| `FilesystemSafetyChecksContainmentAtTheModuleSeam:66` | Assert.False(PackageFileSystemSafety.IsWithinDirectory(root, outside, allowRoot: false)) | `ProbeModuleTests.FilesystemSafetyChecksContainmentAtTheModuleSeam` |
| `IncompleteAnalysisNormalizesReasonsDeterministically:79` | Assert.Equal(ExpectedIncompleteReasons, normalized) | `ProbeModuleTests.IncompleteAnalysisNormalizesReasonsDeterministically` |
| `RestoreGraphComponentsPreserveTargetPackageProvenance:92` | Assert.NotNull(metadata) | `ProbeModuleTests.GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence`, `ProbeModuleTests.RestoreGraphComponentsPreserveTargetPackageProvenance` |
| `RestoreGraphComponentsPreserveTargetPackageProvenance:104` | Assert.Empty(incomplete) | `ProbeModuleTests.CapabilityClassifierOwnsAssetContextAndApplicability`, `ProbeModuleTests.GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence`, `ProbeModuleTests.RestoreGraphComponentsPreserveTargetPackageProvenance` |
| `RestoreGraphComponentsPreserveTargetPackageProvenance:105` | Example.Package/1.0.0 | `ProbeModuleTests.RestoreGraphComponentsPreserveTargetPackageProvenance` |
| `RestoreGraphComponentsPreserveTargetPackageProvenance:105` | Assert.Single(Assert.Single(targets.Targets).Packages) | `ProbeModuleTests.RestoreGraphComponentsPreserveTargetPackageProvenance` |
| `RestoreGraphComponentsPreserveTargetPackageProvenance:105` | Assert.Single(targets.Targets) | `ProbeModuleTests.RestoreGraphComponentsPreserveTargetPackageProvenance` |
| `RestoreGraphComponentsPreserveTargetPackageProvenance:106` | Assert.Single(targets.ResolvedPackages) | `ProbeModuleTests.RestoreGraphComponentsPreserveTargetPackageProvenance` |
| `GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence:118` | Assert.NotNull(metadata) | `ProbeModuleTests.GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence`, `ProbeModuleTests.RestoreGraphComponentsPreserveTargetPackageProvenance` |
| `GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence:139` | Assert.Empty(incomplete) | `ProbeModuleTests.CapabilityClassifierOwnsAssetContextAndApplicability`, `ProbeModuleTests.GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence`, `ProbeModuleTests.RestoreGraphComponentsPreserveTargetPackageProvenance` |
| `GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence:140` | Assert.Empty(result.Imports) | `ProbeModuleTests.GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence` |
| `GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence:141` | Test.csproj.nuget.g.props | `ProbeModuleTests.GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence` |
| `GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence:142` | Test.csproj.nuget.g.targets | `ProbeModuleTests.GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence` |
| `CapabilityClassifierOwnsAssetContextAndApplicability:149` | buildMultiTargeting/Example.props | `ProbeModuleTests.CapabilityClassifierOwnsAssetContextAndApplicability` |
| `CapabilityClassifierOwnsAssetContextAndApplicability:150` | Assert.Equal(CapabilityKind.BuildMultiTargeting, projectAsset.Capability) | `ProbeModuleTests.CapabilityClassifierOwnsAssetContextAndApplicability` |
| `CapabilityClassifierOwnsAssetContextAndApplicability:151` | Assert.Equal(SurfaceContextKind.Project, projectAsset.Context) | `ProbeModuleTests.CapabilityClassifierOwnsAssetContextAndApplicability` |
| `CapabilityClassifierOwnsAssetContextAndApplicability:155` | 1.0.0 | `ProbeModuleTests.CapabilityClassifierOwnsAssetContextAndApplicability` |
| `CapabilityClassifierOwnsAssetContextAndApplicability:176` | Assert.True(active) | `ProbeModuleTests.CapabilityClassifierOwnsAssetContextAndApplicability` |
| `CapabilityClassifierOwnsAssetContextAndApplicability:177` | Assert.Null(reason) | `ProbeModuleTests.CapabilityClassifierOwnsAssetContextAndApplicability` |
| `CapabilityClassifierOwnsAssetContextAndApplicability:178` | Assert.Empty(incomplete) | `ProbeModuleTests.CapabilityClassifierOwnsAssetContextAndApplicability`, `ProbeModuleTests.GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence`, `ProbeModuleTests.RestoreGraphComponentsPreserveTargetPackageProvenance` |
| `FilesystemComponentOwnsPresenceAndResolvedPathEvidence:203` | Assert.True(present.Present) | `ProbeModuleTests.FilesystemComponentOwnsPresenceAndResolvedPathEvidence` |
| `FilesystemComponentOwnsPresenceAndResolvedPathEvidence:204` | Assert.True(File.Exists(present.PhysicalPath)) | `ProbeModuleTests.FilesystemComponentOwnsPresenceAndResolvedPathEvidence` |
| `FilesystemComponentOwnsPresenceAndResolvedPathEvidence:205` | Assert.False(missing.Present) | `ProbeModuleTests.FilesystemComponentOwnsPresenceAndResolvedPathEvidence` |
| `FilesystemComponentOwnsPresenceAndResolvedPathEvidence:206` | unsafe package path | `ProbeModuleTests.FilesystemComponentOwnsPresenceAndResolvedPathEvidence` |
| `IncompleteAnalysisOwnsBudgetFailureConversion:215` | metadata | `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `IncompleteAnalysisOwnsBudgetFailureConversion:218` | Assert.False(result.IsComplete) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.MissingReachablePackageAssetFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `IncompleteAnalysisOwnsBudgetFailureConversion:219` | Assert.Empty(result.Entries) | `ProbeAnalysisTests.MalformedRestoreDocumentsFailClosed`, `ProbeAnalysisTests.MissingAssetsFileFailsClosed`, `ProbeAnalysisTests.OversizedRestoreFileFailsClosed`, `ProbeAnalysisTests.TraversalLookingPackageAssetFailsClosed`, `ProbeAnalysisTests.ValidEmptyPackageGraphProducesACompleteSurface`, `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `IncompleteAnalysisOwnsBudgetFailureConversion:220` | Assert.Equal(failure.Message, Assert.Single(result.IncompleteReasons)) | `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |
| `IncompleteAnalysisOwnsBudgetFailureConversion:220` | Assert.Single(result.IncompleteReasons) | `ProbeModuleTests.IncompleteAnalysisOwnsBudgetFailureConversion` |

### ReportsTests.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Cli.Tests/ReportsTests.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `IncompleteAnalysisUsesPs007:25` | Assert.Single(report.Diagnostics) | `ReportsTests.IncompleteAnalysisUsesPs007` |
| `IncompleteAnalysisUsesPs007:26` | PS007 | `ReportsTests.IncompleteAnalysisUsesPs007` |

### ScenarioSupport.BaselinePersistence.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Cli.Tests/ScenarioSupport.BaselinePersistence.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `RunBaselineContractRegression:27` | A sibling solution project identity did not round-trip through the baseline contract. | `BaselinePersistenceTests.BaselineWritesRemainAtomicAndFailClosed` |
| `RunBaselineContractRegression:38` | A rejected baseline write did not preserve the previous baseline. | `BaselinePersistenceTests.BaselineWritesRemainAtomicAndFailClosed` |
| `RunBaselineContractRegression:55` | An oversized baseline failure replaced the previous baseline. | `BaselinePersistenceTests.BaselineWritesRemainAtomicAndFailClosed` |
| `RunBaselineContractRegression:69` | --project bypassed validation of a missing primary input. | `ProjectSelectionTests.ProjectSelectionValidatesInputAndMembership` |
| `RunBaselineContractRegression:83` | --project did not narrow a directory with more than 128 members. | `ProjectSelectionTests.ProjectSelectionValidatesInputAndMembership` |
| `RunBaselineContractRegression:88` | --project accepted a project outside a large directory input. | `ProjectSelectionTests.ProjectSelectionValidatesInputAndMembership` |
| `RunBaselineTransactionRegression:107` | The transaction baseline seed did not succeed. | `BaselineAliasTests.AssetsOutputAliasPreservesRestoreDocument`, `BaselinePersistenceTests.ExistingBaselineAtSizeBoundaryCanBeReplaced`, `BaselineRollbackTests.OutputOverflowRestoresPreviousBaseline` |
| `RunBaselineTransactionRegression:113` | A baseline output equal to project.assets.json was accepted. | `BaselineAliasTests.AssetsOutputAliasPreservesRestoreDocument` |
| `RunBaselineTransactionRegression:115` | An aliased baseline attempt changed project.assets.json. | `BaselineAliasTests.AssetsOutputAliasPreservesRestoreDocument` |
| `RunBaselineTransactionRegression:122` | The hardlink alias regression could not create a hardlink. | `BaselineAliasTests.HardlinkOutputAliasesPreserveRestoreDocument` |
| `RunBaselineTransactionRegression:123` | The multiple-hardlink alias regression could not create a second hardlink. | `BaselineAliasTests.HardlinkOutputAliasesPreserveRestoreDocument` |
| `RunBaselineTransactionRegression:128` | A hardlink alias to project.assets.json was accepted. | `BaselineAliasTests.HardlinkOutputAliasesPreserveRestoreDocument` |
| `RunBaselineTransactionRegression:130` | A hardlink alias rejection changed project.assets.json. | `BaselineAliasTests.HardlinkOutputAliasesPreserveRestoreDocument` |
| `RunBaselineTransactionRegression:146` | A symlink alias to project.assets.json was accepted. | `BaselineAliasTests.SymlinkOutputAliasPreservesRestoreDocument` |
| `RunBaselineTransactionRegression:148` | A symlink alias rejection changed project.assets.json. | `BaselineAliasTests.SymlinkOutputAliasPreservesRestoreDocument` |
| `RunBaselineTransactionRegression:160` | A baseline output equal to a generated restore import was accepted. | `BaselineAliasTests.GeneratedImportOutputAliasPreservesRestoreDocument` |
| `RunBaselineTransactionRegression:162` | An aliased baseline attempt changed generated restore evidence. | `BaselineAliasTests.GeneratedImportOutputAliasPreservesRestoreDocument` |
| `RunBaselineTransactionRegression:166` | A pre-existing baseline exactly at the size limit was rejected. | `BaselinePersistenceTests.ExistingBaselineAtSizeBoundaryCanBeReplaced` |
| `RunBaselineTransactionRegression:172` | A pre-existing oversized baseline was accepted. | `BaselinePersistenceTests.OversizedBaselinePreservesPreviousBytes` |
| `RunBaselineTransactionRegression:174` | Oversized baseline rejection changed prior data. | `BaselinePersistenceTests.OversizedBaselinePreservesPreviousBytes` |
| `RunBaselineTransactionRegression:179` | A baseline output equal to the selected project was accepted. | `BaselineAliasTests.SelectedProjectOutputAliasPreservesProject` |
| `RunBaselineTransactionRegression:181` | A project-alias rejection changed the project. | `BaselineAliasTests.SelectedProjectOutputAliasPreservesProject` |
| `RunBaselineTransactionRegression:189` | A read-only baseline output was accepted. | `BaselinePersistenceTests.ReadOnlyBaselinePreservesPreviousBytes` |
| `RunBaselineTransactionRegression:191` | A read-only baseline failure changed prior data. | `BaselinePersistenceTests.ReadOnlyBaselinePreservesPreviousBytes` |
| `RunBaselineTransactionRegression:207` | An actual-size {format} output overflow returned exit {exitCode}. | `BaselineRollbackTests.OutputOverflowRestoresPreviousBaseline` |
| `RunBaselineTransactionRegression:215` | An actual-size {format} output overflow replaced the approved baseline. | `BaselineRollbackTests.OutputOverflowRestoresPreviousBaseline` |
| `RunBaselineTransactionRegression:216` | An actual-size {format} output overflow emitted partial success output. | `BaselineRollbackTests.OutputOverflowRestoresPreviousBaseline` |
| `RunBaselineTransactionRegression:227` | A serializer failure did not return exit 2. | `BaselineRollbackTests.SerializationAndSinkFailuresRestorePreviousBaseline` |
| `RunBaselineTransactionRegression:236` | A serializer failure did not roll back the baseline transaction. | `BaselineRollbackTests.SerializationAndSinkFailuresRestorePreviousBaseline` |
| `RunBaselineTransactionRegression:252` | An output sink failure did not return exit 2. | `BaselineRollbackTests.SerializationAndSinkFailuresRestorePreviousBaseline` |
| `RunBaselineTransactionRegression:261` | An output sink failure did not roll back the baseline transaction. | `BaselineRollbackTests.SerializationAndSinkFailuresRestorePreviousBaseline` |
| `RunTerminalIncompleteReportRegression:282` | The late strict-content baseline seed did not succeed. | `BaselinePersistenceTests.IncompleteAnalysisPreservesTheExistingBaseline` |
| `RunTerminalIncompleteReportRegression:288` | Late strict-baseline mismatch did not return PS007 for {format}. | `BaselinePersistenceTests.IncompleteAnalysisPreservesTheExistingBaseline` |
| `RunTerminalIncompleteReportRegression:290` | late strict-baseline mismatch | `BaselinePersistenceTests.IncompleteAnalysisPreservesTheExistingBaseline` |
| `RunTerminalIncompleteReportRegression:294` | Late strict-baseline mismatch omitted incompleteReasons from JSON. | `BaselinePersistenceTests.IncompleteAnalysisPreservesTheExistingBaseline` |
| `RunBaselineJsonBoundaryRegression:313` | Canonical baseline write-read-write bytes were not stable. | `BaselinePersistenceTests.BaselineJsonBoundariesAreValidated` |

### ScenarioSupport.CliAndTelemetry.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Cli.Tests/ScenarioSupport.CliAndTelemetry.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `RunStrictContentEligibilityMatrixTests:45` | Strict-content eligibility mismatch for {capability}, present={present}, active={active}. | `ReportsTests.StrictContentEligibilityMatrix` |
| `RunIdentityAndReportContractTests:55` | A version-only update changed non-strict capability identity. | `ReportsTests.IdentityAndReportContracts` |
| `RunIdentityAndReportContractTests:57` | A version-only update changed strict capability identity. | `ReportsTests.IdentityAndReportContracts` |
| `RunIdentityAndReportContractTests:62` | A version bump with changed content did not produce only PS005. | `ReportsTests.IdentityAndReportContracts` |
| `RunIdentityAndReportContractTests:67` | A new active path did not produce scoped capability diagnostics. | `ReportsTests.IdentityAndReportContracts` |
| `RunIdentityAndReportContractTests:71` | A package identity change was incorrectly treated as a version-only update. | `ReportsTests.IdentityAndReportContracts` |
| `RunIdentityAndReportContractTests:75` | A direct/transitive relationship change was incorrectly ignored. | `ReportsTests.IdentityAndReportContracts` |
| `RunIdentityAndReportContractTests:81` | Default report omitted independent state or target context. | `ReportsTests.IdentityAndReportContracts` |
| `RunIdentityAndReportContractTests:86` | Successful scan SARIF silently discarded classified surface facts. | `ReportsTests.IdentityAndReportContracts` |
| `RunParserMessageRegression:123` | Missing option value did not return exit code 2. | `CliParsingTests.ParserErrorsAreStableAndActionable` |
| `RunParserMessageRegression:131` | Missing option value lost its safe specific parser message or echoed input text. | `CliParsingTests.ParserErrorsAreStableAndActionable` |
| `RunParserTokenPrivacyRegression:164` | A token privacy parser case did not return exit code 2. | `TelemetryTests.ParserTokensAreNeverReported` |
| `RunParserTokenPrivacyRegression:173` | Parser diagnostic leaked untrusted token data for {format} output mode. | `TelemetryTests.ParserTokensAreNeverReported` |
| `RunTelemetryEligibilityTests:194` | Successful baseline creation did not count activation. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:197` | Unchanged check did not count activation. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:209` | A successful baseline with no resolved packages incorrectly counted activation. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:211` | A successful check with no resolved packages incorrectly counted activation. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:238` | Completed exit-1 surface difference did not count activation. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:244` | PS007 analysis incorrectly counted activation. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:247` | Invalid invocation incorrectly counted activation. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:251` | The per-invocation telemetry opt-out did not suppress activation. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:256` | The removed --telemetry option without a value was accepted. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:260` | The removed --telemetry option with an empty value was accepted. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:267` | The removed --telemetry {value} option was accepted. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |
| `RunTelemetryEligibilityTests:272` | The removed --telemetry={value} option was accepted. | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` |

### ScenarioSupport.FilesystemSafety.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Cli.Tests/ScenarioSupport.FilesystemSafety.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `RunPackageIdentityAndPathRegression:37` | A package library path from a different physical package identity was attributed successfully. | `PackageIdentityTests.PackageIdentityAndAssetPathsAreCanonical` |
| `RunPackageIdentityAndPathRegression:49` | Equivalent NuGet version representations were not resolved to the physical package root. | `PackageIdentityTests.PackageIdentityAndAssetPathsAreCanonical` |
| `RunPackageIdentityAndPathRegression:50` | Equivalent normalized prerelease versions were not treated as the same package identity. | `PackageIdentityTests.PackageIdentityAndAssetPathsAreCanonical` |
| `RunPackageIdentityAndPathRegression:68` | A case-sensitive filesystem accepted a wrong-case generated import. | `PackageIdentityTests.PackageIdentityAndAssetPathsAreCanonical` |
| `RunPackageIdentityAndPathRegression:72` | Case-sensitive filesystem identity comparison collapsed distinct project or asset paths. | `PackageIdentityTests.PackageIdentityAndAssetPathsAreCanonical` |
| `RunPackageIdentityAndPathRegression:80` |  \|  | `PackageIdentityTests.PackageIdentityAndAssetPathsAreCanonical` |
| `RunPackageIdentityAndPathRegression:84` | Case-insensitive filesystem identity comparison rejected an equivalent project or asset path. | `PackageIdentityTests.PackageIdentityAndAssetPathsAreCanonical` |
| `RunMixedFilesystemIdentityRegression:158` | , before.IncompleteReasons)} | `FilesystemIdentityTests.PackageAndProjectIdentityUseTheirOwnFilesystem` |
| `RunMixedFilesystemIdentityRegression:161` | {scenario}/{variant.Name}: package-root casing used the process or project volume instead of the package cache. | `FilesystemIdentityTests.PackageAndProjectIdentityUseTheirOwnFilesystem` |
| `RunMixedFilesystemIdentityRegression:171` | {scenario}/{variant.Name}: correct-case package import was not active. | `FilesystemIdentityTests.PackageAndProjectIdentityUseTheirOwnFilesystem` |
| `RunMixedFilesystemIdentityRegression:177` | {scenario}/{variant.Name}: normal and strict transitions did not follow package-cache casing. | `FilesystemIdentityTests.PackageAndProjectIdentityUseTheirOwnFilesystem` |
| `RunMixedFilesystemIdentityRegression:187` | {scenario}/{variant.Name}: classifier identity grouping used the wrong root's casing rules. | `FilesystemIdentityTests.PackageAndProjectIdentityUseTheirOwnFilesystem` |
| `RunMixedFilesystemIdentityRegression:196` | {scenario}/{variant.Name}: project identity used package-cache or process-directory casing. | `FilesystemIdentityTests.PackageAndProjectIdentityUseTheirOwnFilesystem` |
| `RunMixedFilesystemIdentityRegression:200` | {scenario}: NuGet package ID/version identity stopped being case-insensitive. | `FilesystemIdentityTests.PackageAndProjectIdentityUseTheirOwnFilesystem` |
| `RunMixedFilesystemIdentityRegression:217` | {scenario}/{name}: baseline duplicate validation used working-directory semantics or disagreed with scan/check (exit {check.ExitCode}). Output: {check.Output} | `FilesystemIdentityTests.BaselineDuplicateIdentityUsesSelectedRoots` |
| `RunAncestorCanonicalizationRegression:275` |  \|  | `FilesystemIdentityTests.AncestorAliasesAreCanonicalized` |
| `RunReparsePointRegression:294` | A supported filesystem link escape was not rejected. | `PackageContainmentTests.ReparsePointsAreRejected` |
| `RunReparsePointRegression:321` | A rejected package-root link was opened or hashed through a fallback path. | `PackageContainmentTests.ReparsePointsAreRejected` |
| `RunReparsePointRegression:345` | An interior directory reparse point was not rejected. | `PackageContainmentTests.ReparsePointsAreRejected` |
| `RunReparsePointRegression:374` |  \|  | `PackageContainmentTests.ReparsePointsAreRejected` |
| `RunReparsePathVariantRegression:453` | An ancestor reparse point was rejected for {variant.Name}. | `PackageContainmentTests.ReparsePathVariantsAreRejected` |
| `RunReparsePathVariantRegression:457` | The {state} reparse point was not rejected for {variant.Name}. | `PackageContainmentTests.ReparsePathVariantsAreRejected` |
| `RunReachablePackageAliasRegression:535` | The preflight package-input set omitted a reachable package inventory file or fallback-root asset. | `BaselineAliasTests.ReachableInputInventoryAllowsDistinctOutput` |
| `RunReachablePackageAliasRegression:537` | The preflight package-input set omitted a reachable transitive package asset from the RID graph. | `BaselineAliasTests.ReachableInputInventoryAllowsDistinctOutput` |
| `RunReachablePackageAliasRegression:541` | The reachable package alias fixture could not create a valid baseline seed. | `BaselineAliasTests.ReachableInputInventoryAllowsDistinctOutput` |
| `RunReachablePackageAliasRegression:548` | A genuinely distinct baseline output was rejected while package inputs were present. | `BaselineAliasTests.ReachableInputInventoryAllowsDistinctOutput` |
| `RunReachablePackageAliasRegression:554` | A lexical alias to reachable package input '{relative}' was accepted. | `BaselineAliasTests.ReachablePackageAliasesAreRejected` |
| `RunReachablePackageAliasRegression:556` | A lexical package-input alias attempt changed '{relative}'. | `BaselineAliasTests.ReachablePackageAliasesAreRejected` |
| `RunReachablePackageAliasRegression:561` | A lexical alias to a reachable transitive package input was accepted. | `BaselineAliasTests.ReachablePackageAliasesAreRejected` |
| `RunReachablePackageAliasRegression:563` | A transitive package-input alias attempt changed the package file. | `BaselineAliasTests.ReachablePackageAliasesAreRejected` |
| `RunReachablePackageAliasRegression:568` | The package hardlink alias regression could not create a hardlink. | `BaselineAliasTests.ReachablePackageAliasesAreRejected` |
| `RunReachablePackageAliasRegression:569` | The package multiple-hardlink alias regression could not create a second hardlink. | `BaselineAliasTests.ReachablePackageAliasesAreRejected` |
| `RunReachablePackageAliasRegression:574` | A hardlink alias to a reachable package input was accepted. | `BaselineAliasTests.ReachablePackageAliasesAreRejected` |
| `RunReachablePackageAliasRegression:576` | A package hardlink alias attempt changed the representative package input. | `BaselineAliasTests.ReachablePackageAliasesAreRejected` |
| `RunMacOsRootAliasPredicateRegression:609` | The standard macOS /var to /private/var alias was rejected. | `PlatformPathTests.MacosRootAliasPredicateIsStable` |
| `RunMacOsRootAliasPredicateRegression:616` | A caller-controlled root-level /private/<name> alias was accepted as system-owned. | `PlatformPathTests.MacosRootAliasPredicateIsStable` |
| `RunMacOsRootAliasPredicateRegression:623` | A nested link below the standard /var alias was accepted as system-owned. | `PlatformPathTests.MacosRootAliasPredicateIsStable` |
| `RunMacOsRootAliasPredicateRegression:625` | The macOS root-alias exception ignored a non-macOS platform. | `PlatformPathTests.MacosRootAliasPredicateIsStable` |

### ScenarioSupport.GeneratedImports.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Cli.Tests/ScenarioSupport.GeneratedImports.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `RunInlineTaskConventionRegression:46` | Inline task fixture '{name}' was misclassified. | `PackageMsBuildXmlTests.InlineTaskConventionIsDetected` |
| `RunLanguageConventionRegression:69` | , combinedResult.IncompleteReasons)} | `CompilerExtensionTests.CompilerExtensionLanguageConventionsAreApplied` |
| `RunLanguageConventionRegression:75` | Versioned analyzer assets did not fail closed when consuming compiler context was unavailable. | `CompilerExtensionTests.CompilerExtensionLanguageConventionsAreApplied` |
| `RunLanguageConventionRegression:77` | The public CLI did not pass explicit compiler applicability context through to analysis. | `CompilerExtensionTests.CompilerExtensionLanguageConventionsAreApplied` |
| `RunLanguageConventionRegression:101` | , result.IncompleteReasons)} | `CompilerExtensionTests.CompilerExtensionLanguageConventionsAreApplied` |
| `RunLanguageConventionRegression:122` | contentFiles convention case '{testCase.Language}' was misclassified. | `CompilerExtensionTests.CompilerExtensionLanguageConventionsAreApplied` |
| `RunLanguageConventionRegression:132` | Malformed contentFiles {malformed} metadata was accepted. | `CompilerExtensionTests.CompilerExtensionLanguageConventionsAreApplied` |
| `RunApplicabilityHardeningRegressions:148` | Package identity casing variation was not resolved case-insensitively. | `ImportApplicabilityTests.ApplicabilityAndPresentStateAreConservative` |
| `RunApplicabilityHardeningRegressions:157` | Unicode package-relative filename was not classified. | `ImportApplicabilityTests.ApplicabilityAndPresentStateAreConservative` |
| `RunApplicabilityHardeningRegressions:166` | Duplicate package assets were silently accepted. | `ImportApplicabilityTests.ApplicabilityAndPresentStateAreConservative` |
| `RunApplicabilityHardeningRegressions:179` | Language-specific compiler applicability did not fail closed when language evidence was unavailable. | `ImportApplicabilityTests.ApplicabilityAndPresentStateAreConservative` |
| `RunApplicabilityHardeningRegressions:186` | Unknown project language unnecessarily failed a language-independent graph. | `ImportApplicabilityTests.ApplicabilityAndPresentStateAreConservative` |
| `RunNestedImportRegression:220` | , baseline.IncompleteReasons)} | `NestedImportTests.NestedImportsAreAnalyzed` |
| `RunNestedImportRegression:225` | A nested helper-only content change was not detected. | `NestedImportTests.NestedImportsAreAnalyzed` |
| `RunNestedImportRegression:230` | A missing arbitrary-path nested import was not fail-closed. | `NestedImportTests.NestedImportsAreAnalyzed` |
| `RunNestedImportRegression:235` | A bounded static import cycle was not handled deterministically. | `NestedImportTests.NestedImportsAreAnalyzed` |
| `RunNestedImportRegression:240` | An unsupported dynamic nested import was not fail-closed without disclosing its expression. | `NestedImportTests.NestedImportsAreAnalyzed` |
| `RunProjectExtensionsRegression:264` | , generatedResult.IncompleteReasons)} | `GeneratedImportProvenanceTests.ProjectExtensionsAreResolved` |
| `RunProjectExtensionsRegression:285` | , validNamespaceResult.IncompleteReasons)} | `GeneratedImportProvenanceTests.ProjectExtensionsAreResolved` |
| `RunProjectExtensionsRegression:317` | , packageResult.IncompleteReasons)} | `GeneratedImportProvenanceTests.ProjectExtensionsAreResolved` |
| `RunInvalidMsBuildContextRegression:347` | , result.IncompleteReasons)} | `MsBuildContextTests.GeneratedImportContextMustBeSupported` |
| `RunInvalidMsBuildContextRegression:377` | , result.IncompleteReasons)} | `MsBuildContextTests.NestedImportContextMustBeSupported` |
| `RunInvalidMsBuildContextRegression:406` | , result.IncompleteReasons)} | `MsBuildContextTests.PrimitiveContextMustBeSupported` |
| `RunPackageXmlSchemaRegression:456` | , valid.IncompleteReasons)} | `PackageMsBuildXmlTests.ConsumedXmlAttributeSpellingMustBeCanonical`, `PackageMsBuildXmlTests.ConsumedXmlElementSpellingMustBeCanonical`, `PackageMsBuildXmlTests.PackageMsbuildXmlMustMatchTheSupportedSchema` |
| `RunPackageXmlSchemaRegression:457` | The standard MSBuild XML baseline could not be created. | `PackageMsBuildXmlTests.ConsumedXmlAttributeSpellingMustBeCanonical`, `PackageMsBuildXmlTests.ConsumedXmlElementSpellingMustBeCanonical`, `PackageMsBuildXmlTests.PackageMsbuildXmlMustMatchTheSupportedSchema` |
| `RunPackageXmlSchemaRegression:472` | AssertPackageXmlVariantFailure(assets, root, baseline, physicalPath, canonical, name, replacement.Old, replacement.New) | `PackageMsBuildXmlTests.ConsumedXmlElementSpellingMustBeCanonical` |
| `RunPackageXmlSchemaRegression:493` | = | `PackageMsBuildXmlTests.ConsumedXmlAttributeSpellingMustBeCanonical` |
| `RunPackageXmlSchemaRegression:494` | = | `PackageMsBuildXmlTests.ConsumedXmlAttributeSpellingMustBeCanonical` |
| `RunPackageXmlSchemaRegression:495` | AssertPackageXmlDuplicateAttributeFailure(assets, root, baseline, physicalPath, canonical, name, canonicalAttribute, alias) | `PackageMsBuildXmlTests.ConsumedXmlAttributeSpellingMustBeCanonical` |
| `RunNestedPhaseMatrixRegression:540` | , result.IncompleteReasons)} | `ImportConditionTests.NestedImportPhasesRemainDistinct` |
| `RunNestedPhaseMatrixRegression:544` | Nested phase matrix descendant '{path}' was not active and fingerprinted. | `ImportConditionTests.NestedImportPhasesRemainDistinct` |
| `RunNestedPhaseMatrixRegression:551` |  \|  | `ImportConditionTests.FrameworkLiteralsUseStringSemantics` |
| `RunNestedPhaseMatrixRegression:556` | A supported property reference on the right side of a condition was not resolved. | `ImportConditionTests.RightHandPropertyReferenceIsResolved` |
| `RunNestedPhaseMatrixRegression:561` | Quoted or nested AND/OR TargetFramework conditions were not evaluated with the supported string grammar. | `ImportConditionTests.QuotedBooleanConditionsAreResolved` |
| `RunNestedPhaseMatrixRegression:567` | Outer-build TargetFramework condition context was not evaluated independently from target-build context. | `ImportConditionTests.OuterBuildHasIndependentFrameworkContext` |
| `RunNestedPhaseMatrixRegression:572` | An unsupported condition property expansion was treated as a known false branch. | `ImportConditionTests.UnsupportedExpansionRemainsUnknown` |
| `RunImportEdgeBudgetRegression:590` | , within.IncompleteReasons)} | `ImportBudgetTests.ImportGraphWorkIsBounded` |
| `RunImportEdgeBudgetRegression:607` | An 8,193 cumulative generated-import edge graph returned exit {exitCode}. | `ImportBudgetTests.ImportGraphWorkIsBounded` |
| `RunImportEdgeBudgetRegression:614` | The over-budget import graph emitted partial capability success output. | `ImportBudgetTests.ImportGraphWorkIsBounded` |
| `RunGeneratedImportReachabilityRegression:628` | The coherent direct-import fixture could not create its baseline. | `GeneratedImportProvenanceTests.GeneratedImportsMustBeReachable` |
| `RunGeneratedImportReachabilityRegression:635` | direct orphan import | `GeneratedImportProvenanceTests.GeneratedImportsMustBeReachable` |
| `RunGeneratedImportReachabilityRegression:646` | The coherent nested-import fixture could not create its baseline. | `GeneratedImportProvenanceTests.GeneratedImportsMustBeReachable` |
| `RunGeneratedImportReachabilityRegression:661` | nested orphan import | `GeneratedImportProvenanceTests.GeneratedImportsMustBeReachable` |
| `RunGeneratedImportReachabilityRegression:670` | The coherent conditional-import fixture could not create its baseline. | `GeneratedImportProvenanceTests.GeneratedImportsMustBeReachable` |
| `RunGeneratedImportReachabilityRegression:685` | conditional target graph | `GeneratedImportProvenanceTests.GeneratedImportsMustBeReachable` |
| `RunInvocationStructuralBudgetRegression:705` | The within-budget project could not create its recovery baseline. | `InvocationBudgetTests.InvocationStructureIsBounded` |
| `RunInvocationStructuralBudgetRegression:711` | Cumulative target-graph overflow emitted partial {format} scan output. | `InvocationBudgetTests.InvocationStructureIsBounded` |
| `RunInvocationStructuralBudgetRegression:716` | Cumulative target-graph overflow emitted partial {format} baseline output. | `InvocationBudgetTests.InvocationStructureIsBounded` |
| `RunInvocationStructuralBudgetRegression:719` | Cumulative target-graph overflow mutated the baseline for {format} output. | `InvocationBudgetTests.InvocationStructureIsBounded` |
| `RunInvocationStructuralBudgetRegression:723` | Cumulative target-graph overflow emitted a clean-looking {format} check result. | `InvocationBudgetTests.InvocationStructureIsBounded` |
| `RunInvocationStructuralBudgetRegression:730` | --project did not provide the documented recovery path after an aggregate structural-budget failure. | `InvocationBudgetTests.InvocationStructureIsBounded` |
| `RunInvocationStructuralBudgetRegression:732` | --project baseline recovery failed after the aggregate structural-budget failure. | `InvocationBudgetTests.InvocationStructureIsBounded` |
| `RunInvocationStructuralBudgetRegression:734` | --project check recovery failed after the aggregate structural-budget failure. | `InvocationBudgetTests.InvocationStructureIsBounded` |
| `RunInvocationStructuralBudgetRegression:751` | Selecting two projects did not fail closed without partial package-inventory output. | `InvocationBudgetTests.InvocationStructureIsBounded` |
| `RunInvocationStructuralBudgetRegression:753` | A narrowed project did not remain within the package-inventory file budget. | `InvocationBudgetTests.InvocationStructureIsBounded` |
| `RunProjectAggregationRegression:812` | Project-level direct/transitive aggregation was not deterministic or direct-wins. | `AssetClassificationTests.ProjectLevelAssetsAreAggregated` |

### ScenarioSupport.Helpers.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Cli.Tests/ScenarioSupport.Helpers.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `AssertPackageXmlVariantFailure:27` | The XML regression variant '{label}' was not applied. | `PackageXmlAssertions.AssertPackageXmlVariantFailure` |
| `AssertPackageXmlVariantFailure:31` | AssertRestoreIdentityFailure(assets, projectRoot, baseline, label) | `PackageXmlAssertions.AssertPackageXmlVariantFailure` |
| `AssertPackageXmlDuplicateAttributeFailure:51` | The XML duplicate variant '{name}' could not find its canonical attribute. | `PackageXmlAssertions.AssertPackageXmlDuplicateAttributeFailure` |
| `AssertPackageXmlDuplicateAttributeFailure:53` | The XML duplicate variant '{name}' has no complete canonical attribute value. | `PackageXmlAssertions.AssertPackageXmlDuplicateAttributeFailure` |
| `AssertPackageXmlDuplicateAttributeFailure:58` | -duplicate | `PackageXmlAssertions.AssertPackageXmlDuplicateAttributeFailure` |
| `AssertGeneratedImportFailure:86` | {label} emitted a clean or partial {format} scan result. | `IncompleteAnalysisAssertions.AssertGeneratedImportFailure` |
| `AssertGeneratedImportFailure:92` | {label} emitted a clean or partial {format} baseline result. | `IncompleteAnalysisAssertions.AssertGeneratedImportFailure` |
| `AssertGeneratedImportFailure:95` | {label} mutated the baseline. | `IncompleteAnalysisAssertions.AssertGeneratedImportFailure` |
| `AssertGeneratedImportFailure:98` | {label} emitted a clean or policy-diff {format} check result. | `IncompleteAnalysisAssertions.AssertGeneratedImportFailure` |
| `RunSymlinkPackageAliasCase:202` | A {label} symlink alias to a reachable package input was accepted. | `BaselineAliasTests.FileSymlinkCannotReplacePackageInput` |
| `RunSymlinkPackageAliasCase:204` | A {label} symlink alias attempt changed the package input. | `BaselineAliasTests.FileSymlinkCannotReplacePackageInput` |
| `RunDirectorySymlinkPackageAliasCase:222` | A package {label}-directory reparse alias was accepted. | `BaselineAliasTests.DirectorySymlinkCannotReplacePackageInput` |
| `RunDirectorySymlinkPackageAliasCase:224` | A package {label}-directory alias attempt changed the package input. | `BaselineAliasTests.DirectorySymlinkCannotReplacePackageInput` |
| `AssertFormat4DependencyGroupFailure:285` | Format 4 casing-equivalent dependency groups emitted a clean {format} scan. | `IncompleteAnalysisAssertions.AssertFormat4DependencyGroupFailure` |
| `AssertFormat4DependencyGroupFailure:290` | Format 4 casing-equivalent dependency groups emitted a clean {format} baseline. | `IncompleteAnalysisAssertions.AssertFormat4DependencyGroupFailure` |
| `AssertFormat4DependencyGroupFailure:292` | Format 4 casing-equivalent dependency groups mutated the baseline. | `IncompleteAnalysisAssertions.AssertFormat4DependencyGroupFailure` |
| `AssertFormat4DependencyGroupFailure:296` | Format 4 casing-equivalent dependency groups emitted a clean {format} check. | `IncompleteAnalysisAssertions.AssertFormat4DependencyGroupFailure` |
| `AssertValidRestoreMetadata:305` | , analyzed.IncompleteReasons)} | `IncompleteAnalysisAssertions.AssertValidRestoreMetadata` |
| `AssertValidRestoreMetadata:309` | {label} scan failed: {scan.Output} | `IncompleteAnalysisAssertions.AssertValidRestoreMetadata` |
| `AssertValidRestoreMetadata:311` | {label} baseline failed. | `IncompleteAnalysisAssertions.AssertValidRestoreMetadata` |
| `AssertValidRestoreMetadata:314` | {label} check failed: {check.Output} | `IncompleteAnalysisAssertions.AssertValidRestoreMetadata` |
| `RunDependencyRequirementRegression:438` | AssertRestoreIdentityFailure(path, scratch, baselinePath, name) | `DependencyRequirementTests.EachDependencyRequirementMustResolve`, `RestoreConsistencyTests.FrameworkMetadataMustAgree`, `RestoreConsistencyTests.MalformedPackageIdentitiesFailClosed`, `RestoreIdentitySetTests.EveryIdentitySetMustAgree` |
| `RunDependencyRequirementRegression:469` | multi-target-rid dependency requirement | `DependencyRequirementTests.DependencyRequirementsMustResolve` |
| `InsertExactPropertyDuplicate:493` | Could not locate exact property token for '{propertyName}'. | `RestoreDocumentFixture.InsertExactPropertyDuplicate` |
| `InsertCaseVariantPropertyDuplicate:501` | Could not locate case-variant property token for '{propertyName}'. | `RestoreDocumentFixture.InsertCaseVariantPropertyDuplicate` |
| `AssertRestoreIdentityFailure:556` | {label} was not rejected before capability filtering. | `IncompleteAnalysisAssertions.AssertRestoreIdentityFailure` |
| `AssertRestoreIdentityFailure:560` | {label} emitted a clean {format} scan. | `IncompleteAnalysisAssertions.AssertRestoreIdentityFailure` |
| `AssertRestoreIdentityFailure:562` |  scan | `IncompleteAnalysisAssertions.AssertRestoreIdentityFailure` |
| `AssertRestoreIdentityFailure:565` | {label} emitted a clean {format} baseline. | `IncompleteAnalysisAssertions.AssertRestoreIdentityFailure` |
| `AssertRestoreIdentityFailure:567` |  baseline | `IncompleteAnalysisAssertions.AssertRestoreIdentityFailure` |
| `AssertRestoreIdentityFailure:568` | {label} mutated the existing baseline. | `IncompleteAnalysisAssertions.AssertRestoreIdentityFailure` |
| `AssertRestoreIdentityFailure:572` | {label} emitted a clean {format} check. | `IncompleteAnalysisAssertions.AssertRestoreIdentityFailure` |
| `AssertRestoreIdentityFailure:574` |  check | `IncompleteAnalysisAssertions.AssertRestoreIdentityFailure` |
| `AssertNoSuccessfulSurfaceOutput:581` | {label} emitted success-looking output. | `IncompleteAnalysisAssertions.AssertNoSuccessfulSurfaceOutput` |
| `AssertNoSuccessfulSurfaceOutput:593` | {label} emitted surface entries. | `IncompleteAnalysisAssertions.AssertNoSuccessfulSurfaceOutput` |
| `AssertNoSuccessfulSurfaceOutput:600` | {label} emitted a non-PS007 SARIF result. | `IncompleteAnalysisAssertions.AssertNoSuccessfulSurfaceOutput` |

### ScenarioSupport.HostileInputs.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Cli.Tests/ScenarioSupport.HostileInputs.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `RunDtdGeneratedPropsRegression:23` | DTD input was not rejected as incomplete. | `HostileInputTests.GeneratedPropsRejectDocumentTypeDeclarations` |
| `RunDeepGeneratedPropsRegression:42` | Deep XML nesting was not bounded during parsing. | `HostileInputTests.GeneratedPropsXmlDepthIsBounded` |
| `RunTraversalAssetPathRegression:51` | Traversal-looking package path was not rejected. | `HostileInputTests.TraversalLookingAssetPathsFailClosed` |
| `RunInvalidAnalyzerMetadataRegression:63` | Invalid compiler metadata was not rejected. | `HostileInputTests.InvalidCompilerMetadataFailsClosed` |
| `RunOversizedRestoreRegression:72` | Oversized metadata was not rejected as incomplete. | `HostileInputTests.OversizedRestoreInputFailsThroughTheCli` |
| `RunOversizedRestoreRegression:73` | The public CLI did not fail closed before materializing oversized restore input. | `HostileInputTests.OversizedRestoreInputFailsThroughTheCli` |
| `RunLargeGraphBudgetRegression:85` | Reachable graph coverage did not classify the complete graph. | `HostileInputTests.LargeReachableGraphStaysWithinTheResourceBudget` |
| `RunLargeGraphBudgetRegression:86` | Reachable graph analysis exceeded the resource test limit: {timer.Elapsed}. | `HostileInputTests.LargeReachableGraphStaysWithinTheResourceBudget` |

### ScenarioSupport.RestoreAndGraph.cs

[Original source](https://github.com/KeelMatrix/PackageSurface/blob/2f195fe07f3a7b45ab2d2a80f6f29b400281dde6/tests/KeelMatrix.PackageSurface.Cli.Tests/ScenarioSupport.RestoreAndGraph.cs)

| Original method and line | Assertion / failure diagnostic | Destination |
| --- | --- | --- |
| `RunDiagnosticPathLeakRegression:49` | {format} output disclosed an absolute cache marker. | `HostileInputTests.DiagnosticsDoNotExposeLocalPaths` |
| `RunDiagnosticPathLeakRegression:50` | {format} stderr disclosed an absolute cache marker. | `HostileInputTests.DiagnosticsDoNotExposeLocalPaths` |
| `RunDiagnosticPathLeakRegression:51` | {format} output disclosed an unsupported condition marker. | `HostileInputTests.DiagnosticsDoNotExposeLocalPaths` |
| `RunDiagnosticPathLeakRegression:52` | {format} stderr disclosed an unsupported condition marker. | `HostileInputTests.DiagnosticsDoNotExposeLocalPaths` |
| `RunAnalyzerExclusionRegression:62` | Direct ExcludeAssets=analyzers was not honored. | `AnalyzerPropagationTests.AnalyzerExclusionsMatchRestoreRules` |
| `RunAnalyzerExclusionRegression:68` | Transitive ExcludeAssets=analyzers was not honored. | `AnalyzerPropagationTests.AnalyzerExclusionsMatchRestoreRules` |
| `RunMalformedAssetsShapeRegression:93` | , result.IncompleteReasons)} | `RestoreSchemaTests.MalformedAssetsShapesFailClosed` |
| `RunMalformedAssetsShapeRegression:118` | A project-node dependency with a scalar value was accepted. | `RestoreSchemaTests.MalformedAssetsShapesFailClosed` |
| `RunMalformedAssetsShapeRegression:133` | , result.IncompleteReasons)} | `RestoreSchemaTests.MalformedAssetsShapesFailClosed` |
| `RunMalformedAssetsShapeRegression:163` | Malformed typed metadata '{name}' was accepted. | `RestoreSchemaTests.MalformedTargetMetadataFailsClosed` |
| `RunMalformedAssetsShapeRegression:200` | Malformed assets shape '{name}' was not fail-closed. | `RestoreSchemaTests.MalformedDocumentShapesFailClosed` |
| `RunMalformedAssetsShapeRegression:214` | Malformed assets CLI invocation returned {cliExitCode}, expected 2. | `RestoreSchemaTests.MalformedAssetsShapesFailClosed` |
| `RunAssetsFormatRegression:235` | A coherent assets format 4 graph was not classified. | `RestoreSchemaTests.AssetsFormatIsValidated`, `RestoreSchemaTests.FormatFourMetadataMustBeComplete` |
| `RunAssetsFormatRegression:257` | Malformed assets format 4 shape '{name}' was not fail-closed. | `RestoreSchemaTests.FormatFourMetadataMustBeComplete` |
| `RunAssetsFormatRegression:264` | The coherent format 4 dependency-group fixture could not create its baseline: exit={coherentBaseline.ExitCode}; output={coherentBaseline.Output} | `RestoreSchemaTests.FormatFourMetadataMustBeComplete` |
| `RunAssetsFormatRegression:266` | AssertFormat4DependencyGroupFailure(path, validBaseline) | `RestoreSchemaTests.FormatFourMetadataMustBeComplete` |
| `RunPackageFoldersValidationRegression:307` | Malformed packageFolders shape '{name}' was accepted. | `RestoreSchemaTests.MalformedPackageFoldersFailClosed` |
| `RunPackageFoldersValidationRegression:327` | , result.IncompleteReasons)} | `RestoreSchemaTests.PackageFolderMetadataIsValidated` |
| `RunPackageFoldersValidationRegression:349` | Mixed valid/invalid packageFolders leaked its absolute cache path in {format} output. | `RestoreSchemaTests.PackageFolderMetadataIsValidated` |
| `RunPackageFoldersValidationRegression:351` | mixed invalid packageFolders | `RestoreSchemaTests.PackageFolderMetadataIsValidated` |
| `RunPackageFoldersValidationRegression:375` | Duplicate/unknown path-marked restore members leaked in {format} output. | `RestoreSchemaTests.PackageFolderMetadataIsValidated` |
| `RunPackageFoldersValidationRegression:377` | path-marked restore members | `RestoreSchemaTests.PackageFolderMetadataIsValidated` |
| `RunCapabilityGroupValidationRegression:419` | Unsupported target asset group '{group}' was accepted as a clean or partial result. | `RestoreSchemaTests.UnsupportedCapabilityGroupsFailClosed` |
| `RunCapabilityGroupValidationRegression:435` | A mixed canonical and unsupported target asset-group set was accepted as a clean or partial result. | `RestoreSchemaTests.CapabilityGroupsAreValidated` |
| `RunCapabilityGroupValidationRegression:469` | An unsupported target asset group on a transitive RID package was accepted as a clean or partial result. | `RestoreSchemaTests.CapabilityGroupsAreValidated` |
| `RunFrameworkMonikerRegression:501` | , result.IncompleteReasons)} | `FrameworkIdentityTests.FrameworkAliasesMatchResolvedTargets` |
| `RunFrameworkMonikerRegression:538` | Format 4 project/restore framework aliases were not reconciled. | `FrameworkIdentityTests.FrameworkMonikersAreCanonicalized` |
| `RunFrameworkMonikerRegression:556` | A declared framework with no equivalent target graph did not fail closed. | `FrameworkIdentityTests.FrameworkMonikersAreCanonicalized` |
| `RunRestoreIdentityCanonicalizationRegression:594` | , valid.IncompleteReasons)} | `RestoreConsistencyTests.DuplicateFrameworkIdentitiesFailClosed`, `RestoreConsistencyTests.FrameworkMetadataMustAgree`, `RestoreConsistencyTests.MalformedPackageIdentitiesFailClosed`, `RestoreConsistencyTests.RestoreIdentitiesReconcileAcrossMetadataSources` |
| `RunRestoreIdentityCanonicalizationRegression:596` | The valid restore-identity fixture could not create its baseline. | `RestoreConsistencyTests.DuplicateFrameworkIdentitiesFailClosed`, `RestoreConsistencyTests.FrameworkMetadataMustAgree`, `RestoreConsistencyTests.MalformedPackageIdentitiesFailClosed`, `RestoreConsistencyTests.RestoreIdentitiesReconcileAcrossMetadataSources` |
| `RunRestoreIdentityCanonicalizationRegression:645` | AssertRestoreIdentityFailure(path, scratch, baselinePath, name) | `DependencyRequirementTests.EachDependencyRequirementMustResolve`, `RestoreConsistencyTests.FrameworkMetadataMustAgree`, `RestoreConsistencyTests.MalformedPackageIdentitiesFailClosed`, `RestoreIdentitySetTests.EveryIdentitySetMustAgree` |
| `RunRestoreIdentityCanonicalizationRegression:657` | The valid restore-identity fixture has no target-alias metadata token. | `RestoreConsistencyTests.RestoreIdentitiesReconcileAcrossMetadataSources` |
| `RunRestoreIdentityCanonicalizationRegression:663` | conflicting duplicate metadata | `RestoreConsistencyTests.RestoreIdentitiesReconcileAcrossMetadataSources` |
| `RunRestoreIdentityCanonicalizationRegression:696` | {duplicateMap.Name} {duplicateKind} duplicate | `RestoreConsistencyTests.DuplicateFrameworkIdentitiesFailClosed` |
| `RunRestoreIdentityCanonicalizationRegression:712` | {duplicateMap.Name} raw exact duplicate | `RestoreConsistencyTests.DuplicateFrameworkIdentitiesFailClosed` |
| `RunRestoreIdentityCanonicalizationRegression:758` | AssertRestoreIdentityFailure(path, scratch, baselinePath, name) | `DependencyRequirementTests.EachDependencyRequirementMustResolve`, `RestoreConsistencyTests.FrameworkMetadataMustAgree`, `RestoreConsistencyTests.MalformedPackageIdentitiesFailClosed`, `RestoreIdentitySetTests.EveryIdentitySetMustAgree` |
| `RunRestoreIdentityCanonicalizationRegression:767` | The valid restore-identity fixture did not recover after invalid identity probes. | `RestoreConsistencyTests.RestoreIdentitiesReconcileAcrossMetadataSources` |
| `RunRestoreIdentityCanonicalizationRegression:803` | Selecting a valid and malformed restore identity together did not fail closed. | `RestoreConsistencyTests.RestoreIdentitiesReconcileAcrossMetadataSources` |
| `RunRestoreIdentityCanonicalizationRegression:807` | --project did not recover the valid restore identity from a malformed sibling: exit={narrowed.ExitCode}; output={narrowed.Output} | `RestoreConsistencyTests.RestoreIdentitiesReconcileAcrossMetadataSources` |
| `RunRestoreJsonStructuralDuplicateRegression:820` | The structural-duplicate baseline could not be created. | `RestoreStructureTests.CaseVariantJsonPropertiesFailClosed`, `RestoreStructureTests.DuplicateJsonPropertiesFailClosed`, `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed`, `RestoreStructureTests.UnknownJsonPropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:859` | AssertRestoreIdentityFailure(path, scratch, baseline, name) | `RestoreStructureTests.CaseVariantJsonPropertiesFailClosed`, `RestoreStructureTests.DuplicateJsonPropertiesFailClosed`, `RestoreStructureTests.UnknownJsonPropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:940` | AssertRestoreIdentityFailure(path, scratch, baseline, name) | `RestoreStructureTests.CaseVariantJsonPropertiesFailClosed`, `RestoreStructureTests.DuplicateJsonPropertiesFailClosed`, `RestoreStructureTests.UnknownJsonPropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:980` | AssertRestoreIdentityFailure(path, scratch, baseline, name) | `RestoreStructureTests.CaseVariantJsonPropertiesFailClosed`, `RestoreStructureTests.DuplicateJsonPropertiesFailClosed`, `RestoreStructureTests.UnknownJsonPropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:1002` | , extensionResult.IncompleteReasons)} | `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:1024` | AssertRestoreIdentityFailure(assets, scratch, baseline, name) | `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:1029` | , xmlExtensionResult.IncompleteReasons)} | `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:1039` | The content metadata baseline could not be created. | `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:1047` | content-file-metadata-alias | `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:1058` | The content unknown-spelling baseline could not be created. | `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:1067` | content-file-metadata-near-spelling | `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed` |
| `RunRestoreJsonStructuralDuplicateRegression:1084` | , contentResult.IncompleteReasons)} | `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed` |
| `RunRestoreOptionalMetadataRegression:1111` | format 3 restore metadata | `RestoreMetadataTests.OptionalRestoreMetadataIsValidated` |
| `RunRestoreOptionalMetadataRegression:1131` | format 4 restore metadata | `RestoreMetadataTests.OptionalRestoreMetadataIsValidated` |
| `RunRestoreOptionalMetadataRegression:1158` | AssertRestoreIdentityFailure(invalidPath, scratch, format3Baseline, name) | `RestoreMetadataTests.OptionalRestoreMetadataIsValidated` |
| `RunRestoreMetadataShapeRegression:1193` | all valid restore metadata shapes (format 3) | `RestoreMetadataTests.MalformedMetadataShapesFailClosed (format: 3)`, `RestoreMetadataTests.SupportedMetadataShapesRoundTrip (format: 3)` |
| `RunRestoreMetadataShapeRegression:1194` | all valid restore metadata shapes (format 4) | `RestoreMetadataTests.MalformedMetadataShapesFailClosed (format: 4)`, `RestoreMetadataTests.SupportedMetadataShapesRoundTrip (format: 4)` |
| `RunRestoreMetadataShapeRegression:1249` | ) | `RestoreMetadataTests.MalformedMetadataShapesFailClosed` |
| `RunReachabilityClosureRegression:1311` | An orphan package with build, analyzer, content, native, and tool assets was classified as reachable. | `PackageReachabilityTests.ReachabilityClosureIncludesOnlyResolvedPackages` |
| `RunReachabilityClosureRegression:1328` | A disconnected multi-package dependency island was classified as reachable. | `PackageReachabilityTests.ReachabilityClosureIncludesOnlyResolvedPackages` |
| `RunReachabilityClosureRegression:1359` |  \|  | `PackageReachabilityTests.ReachabilityClosureIncludesOnlyResolvedPackages` |
| `RunReachabilityClosureRegression:1361` | Reachable transitive and project-reference packages were not classified with the expected relationship. | `PackageReachabilityTests.ReachabilityClosureIncludesOnlyResolvedPackages` |
| `RunReachabilityClosureRegression:1364` |  \|  | `PackageReachabilityTests.ReachabilityClosureIncludesOnlyResolvedPackages` |
| `RunReachabilityClosureRegression:1385` |  \|  | `PackageReachabilityTests.ReachabilityClosureIncludesOnlyResolvedPackages` |
| `RunVersionAwareReachabilityConflictRegression:1408` | The valid {name} baseline could not be created. | `PackageReachabilityTests.TargetVersionsMustMatchDependencyRequirements` |
| `RunVersionAwareReachabilityConflictRegression:1431` | , validResult.IncompleteReasons)} | `PackageReachabilityTests.TargetVersionsMustMatchDependencyRequirements` |
| `RunVersionAwareReachabilityConflictRegression:1436` | AssertRestoreIdentityFailure(assets, root, baseline, name) | `PackageReachabilityTests.TargetVersionsMustMatchDependencyRequirements` |
| `RunVersionAwareReachabilityConflictRegression:1446` | The valid all-surfaces baseline could not be created. | `PackageReachabilityTests.VersionConflictsAreResolvedPerTarget` |
| `RunVersionAwareReachabilityConflictRegression:1477` | all-surfaces-multi-version-selection | `PackageReachabilityTests.VersionConflictsAreResolvedPerTarget` |
| `RunVersionEquivalenceRegression:1485` | NuGet-compatible version '{value}' was rejected. | `PackageVersionTests.VersionEquivalenceUsesNugetSemantics` |
| `RunVersionEquivalenceRegression:1488` | Equivalent numeric package versions did not share one identity. | `PackageVersionTests.VersionEquivalenceUsesNugetSemantics` |
| `RunVersionEquivalenceRegression:1489` | The four-component version boundary was not enforced. | `PackageVersionTests.VersionEquivalenceUsesNugetSemantics` |
| `RunVersionEquivalenceRegression:1492` | Prerelease casing/components or build metadata did not use the canonical identity grammar. | `PackageVersionTests.VersionEquivalenceUsesNugetSemantics` |
| `RunVersionEquivalenceRegression:1512` | Malformed version '{invalid}' was accepted. | `PackageVersionTests.VersionEquivalenceUsesNugetSemantics` |
| `RunVersionEquivalenceRegression:1527` | , result.IncompleteReasons)} | `PackageVersionTests.VersionEquivalenceUsesNugetSemantics` |
| `RunVersionEquivalenceRegression:1533` | A five-component dependency range was accepted. | `PackageVersionTests.VersionEquivalenceUsesNugetSemantics` |
| `RunVersionEquivalenceRegression:1540` | Malformed NuGet version range '{invalid}' was accepted. | `PackageVersionTests.VersionEquivalenceUsesNugetSemantics` |
| `RunMalformedVersionEndToEndRegression:1580` | The all-capability version fixture could not create its baseline:  | `PackageVersionTests.AllCapabilityVersionFixtureCreatesBaseline` |
| `RunMalformedVersionEndToEndRegression:1590` | malformed package identity  | `PackageVersionTests.MalformedPackageKeysFailThroughTheCli` |
| `RunMalformedVersionEndToEndRegression:1604` | malformed library package path | `PackageVersionTests.MalformedLibraryPathFailsThroughTheCli` |
| `RunMalformedVersionEndToEndRegression:1617` | malformed direct dependency range | `PackageVersionTests.MalformedDirectRangeFailsThroughTheCli` |
| `RunMalformedVersionEndToEndRegression:1633` | malformed target dependency range | `PackageVersionTests.MalformedTargetRangeFailsThroughTheCli` |
| `RunMalformedVersionEndToEndRegression:1655` | malformed project dependency range | `PackageVersionTests.MalformedProjectRangeFailsThroughTheCli` |
| `RunMalformedVersionEndToEndRegression:1678` | The valid format 4 version fixture could not create its baseline:  | `PackageVersionTests.MalformedFormatFourRangeFailsThroughTheCli` |
| `RunMalformedVersionEndToEndRegression:1684` | malformed format 4 dependency requirement | `PackageVersionTests.MalformedFormatFourRangeFailsThroughTheCli` |
| `RunMalformedVersionEndToEndRegression:1700` | malformed generated import version  | `PackageVersionTests.MalformedGeneratedImportVersionFailsThroughTheCli` |
| `RunMalformedVersionEndToEndRegression:1720` | Malformed baseline version was accepted in {format} output. | `PackageVersionTests.MalformedBaselineVersionFailsInEveryReport` |
| `RunMalformedVersionEndToEndRegression:1722` | malformed baseline version  | `PackageVersionTests.MalformedBaselineVersionFailsInEveryReport` |
| `RunRestoreIdentitySetCompletenessRegression:1794` | AssertRestoreIdentityFailure(path, scratch, baselinePath, name) | `DependencyRequirementTests.EachDependencyRequirementMustResolve`, `RestoreConsistencyTests.FrameworkMetadataMustAgree`, `RestoreConsistencyTests.MalformedPackageIdentitiesFailClosed`, `RestoreIdentitySetTests.EveryIdentitySetMustAgree` |

## Shared assertion consumers

The utilities below are reused only for related fail-closed, schema and privacy cases. Their callers remain discovered test methods with their own input mutations and scenario assertions.

| Shared assertion | Discoverable callers |
| --- | --- |
| `IncompleteAnalysisAssertions.AssertFormat4DependencyGroupFailure` | `RestoreSchemaTests.FormatFourMetadataMustBeComplete` |
| `IncompleteAnalysisAssertions.AssertGeneratedImportFailure` | `GeneratedImportProvenanceTests.GeneratedImportsMustBeReachable` |
| `IncompleteAnalysisAssertions.AssertNoSuccessfulSurfaceOutput` | `BaselinePersistenceTests.IncompleteAnalysisPreservesTheExistingBaseline`, `DependencyRequirementTests.DependencyRequirementsMustResolve`, `DependencyRequirementTests.EachDependencyRequirementMustResolve`, `PackageMsBuildXmlTests.ConsumedXmlAttributeSpellingMustBeCanonical`, `PackageMsBuildXmlTests.ConsumedXmlElementSpellingMustBeCanonical`, `PackageReachabilityTests.TargetVersionsMustMatchDependencyRequirements`, `PackageReachabilityTests.VersionConflictsAreResolvedPerTarget`, `PackageVersionTests.MalformedBaselineVersionFailsInEveryReport`, `PackageVersionTests.MalformedDirectRangeFailsThroughTheCli`, `PackageVersionTests.MalformedFormatFourRangeFailsThroughTheCli`, `PackageVersionTests.MalformedGeneratedImportVersionFailsThroughTheCli`, `PackageVersionTests.MalformedLibraryPathFailsThroughTheCli`, `PackageVersionTests.MalformedPackageKeysFailThroughTheCli`, `PackageVersionTests.MalformedProjectRangeFailsThroughTheCli`, `PackageVersionTests.MalformedTargetRangeFailsThroughTheCli`, `RestoreConsistencyTests.DuplicateFrameworkIdentitiesFailClosed`, `RestoreConsistencyTests.FrameworkMetadataMustAgree`, `RestoreConsistencyTests.MalformedPackageIdentitiesFailClosed`, `RestoreConsistencyTests.RestoreIdentitiesReconcileAcrossMetadataSources`, `RestoreIdentitySetTests.EveryIdentitySetMustAgree`, `RestoreMetadataTests.MalformedMetadataShapesFailClosed`, `RestoreMetadataTests.OptionalRestoreMetadataIsValidated`, `RestoreSchemaTests.PackageFolderMetadataIsValidated`, `RestoreStructureTests.CaseVariantJsonPropertiesFailClosed`, `RestoreStructureTests.DuplicateJsonPropertiesFailClosed`, `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed`, `RestoreStructureTests.UnknownJsonPropertiesFailClosed` |
| `IncompleteAnalysisAssertions.AssertRestoreIdentityFailure` | `DependencyRequirementTests.DependencyRequirementsMustResolve`, `DependencyRequirementTests.EachDependencyRequirementMustResolve`, `PackageMsBuildXmlTests.ConsumedXmlAttributeSpellingMustBeCanonical`, `PackageMsBuildXmlTests.ConsumedXmlElementSpellingMustBeCanonical`, `PackageReachabilityTests.TargetVersionsMustMatchDependencyRequirements`, `PackageReachabilityTests.VersionConflictsAreResolvedPerTarget`, `PackageVersionTests.MalformedDirectRangeFailsThroughTheCli`, `PackageVersionTests.MalformedFormatFourRangeFailsThroughTheCli`, `PackageVersionTests.MalformedGeneratedImportVersionFailsThroughTheCli`, `PackageVersionTests.MalformedLibraryPathFailsThroughTheCli`, `PackageVersionTests.MalformedPackageKeysFailThroughTheCli`, `PackageVersionTests.MalformedProjectRangeFailsThroughTheCli`, `PackageVersionTests.MalformedTargetRangeFailsThroughTheCli`, `RestoreConsistencyTests.DuplicateFrameworkIdentitiesFailClosed`, `RestoreConsistencyTests.FrameworkMetadataMustAgree`, `RestoreConsistencyTests.MalformedPackageIdentitiesFailClosed`, `RestoreConsistencyTests.RestoreIdentitiesReconcileAcrossMetadataSources`, `RestoreIdentitySetTests.EveryIdentitySetMustAgree`, `RestoreMetadataTests.MalformedMetadataShapesFailClosed`, `RestoreMetadataTests.OptionalRestoreMetadataIsValidated`, `RestoreStructureTests.CaseVariantJsonPropertiesFailClosed`, `RestoreStructureTests.DuplicateJsonPropertiesFailClosed`, `RestoreStructureTests.StructuralDuplicatePropertiesFailClosed`, `RestoreStructureTests.UnknownJsonPropertiesFailClosed` |
| `IncompleteAnalysisAssertions.AssertValidRestoreMetadata` | `RestoreMetadataTests.MalformedMetadataShapesFailClosed`, `RestoreMetadataTests.OptionalRestoreMetadataIsValidated`, `RestoreMetadataTests.SupportedMetadataShapesRoundTrip` |
| `PackageXmlAssertions.AssertPackageXmlDuplicateAttributeFailure` | `PackageMsBuildXmlTests.ConsumedXmlAttributeSpellingMustBeCanonical` |
| `PackageXmlAssertions.AssertPackageXmlVariantFailure` | `PackageMsBuildXmlTests.ConsumedXmlAttributeSpellingMustBeCanonical`, `PackageMsBuildXmlTests.ConsumedXmlElementSpellingMustBeCanonical` |
| `RestoreDocumentFixture.InsertCaseVariantPropertyDuplicate` | `RestoreStructureTests.DuplicateJsonPropertiesFailClosed` |
| `RestoreDocumentFixture.InsertExactPropertyDuplicate` | `RestoreConsistencyTests.DuplicateFrameworkIdentitiesFailClosed`, `RestoreStructureTests.DuplicateJsonPropertiesFailClosed` |

## Probe and retained integration gates

The original 21 Probe tests remain discoverable in `ProbeModuleTests` and `ProbeAnalysisTests`; additional focused policies are tested in `RestorePolicyTests`, `StaticImportConditionTests` and `PackageMsBuildInspectionTests`. The original module tests follow the extracted policy owners without changing their assertions.

| Original integration scenario | Retained gate |
| --- | --- |
| Every seeded capability; TFM, RID and outer-target contexts; deduplication; present versus active | `scripts/run-phase0.ps1` runs `tools/ProbeFixtureVerifier` against each controlled restored fixture |
| Filesystem comparison-cache regression and generated-import file identity | Phase 0 fixture verifier |
| Target-framework/Exists/unknown-property conditions, nested contexts and fail-closed output | Phase 0 condition matrix, plus focused discovered condition/filesystem tests |
| Generated props/targets cross-wiring, phase-specific and TFM-specific variants | Phase 0 fixture verifier, plus `ImportConditionTests` |
| Restore identity coherence | Phase 0 fixture verifier and discovered restore identity/consistency tests |
| Strict-content eligibility | `ReportsTests.StrictContentEligibilityMatrix` and installed-tool checks |
| Package installation; deterministic baseline; changed surface and PS001–PS007 reports | `scripts/run-local-gate.ps1`, isolated installed tool and hardening checks |
| DTD, XML depth, traversal, metadata corruption, graph size, input/output budgets and privacy | Discovered hostile-input/budget tests, Phase 0 and installed-tool hardening |
| Baseline atomicity, prior bytes, hardlinks/reparse containment and platform casing | Discovered persistence/alias/containment/filesystem tests and three-platform CI |
| Package, symbol, dependency audit, no execution/network, docs and release contracts | Complete repository gate |
| Zero discovered tests and a deliberately failing xUnit test | `scripts/test-discovered-tests.ps1 -RunNegativeControls`; both must propagate failure |

CI retains the discovered CLI and Probe TRX results on Windows, Ubuntu and macOS. The discovery guard retains the minimum of 52 CLI and 21 Probe executed cases.
