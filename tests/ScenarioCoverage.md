# Test scenario migration ledger

This ledger was prepared from the two executable test programs at the rejected base revision `75986de6a7832c7bc5348ddb270d6ec265f00c89` before those entry points were removed. Each CLI runner case now has a named discovered xUnit case. The probe program's fixture-specific assertions remain in the dedicated fixture verifier because the Phase 0 gate supplies a different restored consumer graph for each fixture; focused probe and module behavior is also checked by discovered xUnit tests.

## CLI executable-harness scenarios

| Existing scenario | Discovered test or retained gate | Mapping notes |
| --- | --- | --- |
| Diff diagnostics include PS001–PS006 | `CliParsingTests.DiffReportsAllCapabilityChanges` | Checks the complete diagnostic ID set. |
| Strict-content eligibility matrix | `ReportsTests.StrictContentEligibilityMatrix` | Existing matrix assertions remain together. |
| Identity and report contracts | `ReportsTests.IdentityAndReportContracts` | Existing report schema assertions remain together. |
| Incomplete report emits PS007 | `ReportsTests.IncompleteAnalysisUsesPs007` | Focused report case. |
| CLI help clauses and exit-code contract | `CliParsingTests.HelpDocumentsTheStableCommandContract` | Checks the public help clauses. |
| DTD rejection in generated imports | `HostileInputTests.GeneratedPropsRejectDocumentTypeDeclarations` | Separate discovered test. |
| Generated-import XML nesting depth limit | `HostileInputTests.GeneratedPropsXmlDepthIsBounded` | Separate discovered test. |
| Analyzer exclusion rules | `AssetClassificationTests.AnalyzerExclusionsMatchRestoreRules` | Separate discovered test. |
| Malformed assets shape | `RestoreValidationTests.MalformedAssetsShapesFailClosed` | Separate discovered test. |
| Package-folder metadata validation | `RestoreValidationTests.PackageFolderMetadataIsValidated` | Separate discovered test. |
| Assets format validation | `RestoreValidationTests.AssetsFormatIsValidated` | Separate discovered test. |
| Capability-group validation | `RestoreValidationTests.CapabilityGroupsAreValidated` | Separate discovered test. |
| Framework-moniker canonicalization | `RestoreValidationTests.FrameworkMonikersAreCanonicalized` | Separate discovered test. |
| Restore identity canonicalization, format-4 identity-set completeness, and dependency requirement handling | `GraphIdentityTests.RestoreIdentitiesReconcileAcrossMetadataSources` | Grouped because the completeness and dependency cases mutate the same format-4 graph in one scenario. |
| Structural duplicate JSON properties | `RestoreValidationTests.StructuralDuplicatePropertiesFailClosed` | Separate discovered test. |
| Optional restore metadata validation | `RestoreValidationTests.OptionalRestoreMetadataIsValidated` | Separate discovered test. |
| Restore metadata shape validation | `RestoreValidationTests.RestoreMetadataShapesFailClosed` | Separate discovered test. |
| Reachability closure | `GraphIdentityTests.ReachabilityClosureIncludesOnlyResolvedPackages` | Separate discovered test. |
| Version-aware reachability conflicts | `GraphIdentityTests.VersionConflictsAreResolvedPerTarget` | Separate discovered test. |
| NuGet version equivalence | `GraphIdentityTests.VersionEquivalenceUsesNugetSemantics` | Separate discovered test. |
| Malformed version through the public CLI | `GraphIdentityTests.MalformedVersionsFailThroughTheCli` | Retains the end-to-end CLI invocation. |
| Diagnostic path privacy | `HostileInputTests.DiagnosticsDoNotExposeLocalPaths` | Separate discovered test. |
| Applicability and present/active-state matrix | `AssetClassificationTests.ApplicabilityAndPresentStateAreConservative` | Matrix cases remain grouped around the shared applicability contract. |
| Compiler-extension language conventions | `AssetClassificationTests.CompilerExtensionLanguageConventionsAreApplied` | Also covered as a child case in the applicability matrix. |
| Project-level asset aggregation | `AssetClassificationTests.ProjectLevelAssetsAreAggregated` | Also covered as a child case in the applicability matrix. |
| Nested generated imports | `GeneratedImportTests.NestedImportsAreAnalyzed` | Separate discovered test. |
| Nested props/targets phase matrix | `GeneratedImportTests.NestedImportPhasesRemainDistinct` | Matrix cases remain grouped by phase. |
| Package MSBuild XML schema | `GeneratedImportTests.PackageMsbuildXmlMustMatchTheSupportedSchema` | Separate discovered test. |
| Project-extension resolution | `GeneratedImportTests.ProjectExtensionsAreResolved` | Separate discovered test. |
| Unsupported MSBuild contexts | `GeneratedImportTests.UnsupportedMsbuildContextsFailClosed` | Separate discovered test. |
| Generated-import reachability | `GeneratedImportTests.GeneratedImportsMustBeReachable` | Separate discovered test. |
| Import-edge work budget | `GeneratedImportTests.ImportGraphWorkIsBounded` | Separate discovered test. |
| Invocation-structure budget | `GeneratedImportTests.InvocationStructureIsBounded` | Separate discovered test. |
| Inline-task convention | `GeneratedImportTests.InlineTaskConventionIsDetected` | Separate discovered test. |
| Traversal-looking asset paths | `HostileInputTests.TraversalLookingAssetPathsFailClosed` | Separate discovered test. |
| Invalid compiler metadata | `HostileInputTests.InvalidCompilerMetadataFailsClosed` | Separate discovered test. |
| Oversized restore input and CLI fail-closed exit | `HostileInputTests.OversizedRestoreInputFailsThroughTheCli` | Grouped because the same oversized document is checked through the analyzer and public CLI. |
| 300-package graph and analysis-time budget | `HostileInputTests.LargeReachableGraphStaysWithinTheResourceBudget` | Checks completeness, package count, and the existing time ceiling. |
| Ancestor path alias canonicalization | `FilesystemSafetyTests.AncestorAliasesAreCanonicalized` | Separate discovered test. |
| Reparse-point rejection | `FilesystemSafetyTests.ReparsePointsAreRejected` | Separate discovered test. |
| Reparse-point path variants | `FilesystemSafetyTests.ReparsePathVariantsAreRejected` | Separate discovered test. |
| Reachable package file, interior-directory, and ancestor symlink aliases | `FilesystemSafetyTests.ReachablePackageAliasesAreRejected` | Grouped because every alias case checks the same package-root containment invariant. |
| Package identity and asset-path canonicalization | `GraphIdentityTests.PackageIdentityAndAssetPathsAreCanonical` | Separate discovered test. |
| Mixed filesystem identity | `FilesystemSafetyTests.MixedFilesystemIdentityIsConsistent` | Separate discovered test. |
| macOS root-alias predicate | `FilesystemSafetyTests.MacosRootAliasPredicateIsStable` | Separate discovered test. |
| Telemetry eligibility and package-identity privacy | `TelemetryTests.EligibilityDoesNotExposePackageIdentity` | Existing telemetry scenarios share the same isolated hook. |
| Parser error wording | `CliParsingTests.ParserErrorsAreStableAndActionable` | Separate discovered test. |
| Parser-token telemetry privacy | `TelemetryTests.ParserTokensAreNeverReported` | Separate discovered test. |
| Baseline write contract and atomic replacement | `BaselinePersistenceTests.BaselineWritesRemainAtomicAndFailClosed` | The original contract case includes its atomicity and incomplete-report assertions. |
| Baseline transaction preserves prior file | `BaselinePersistenceTests.BaselineTransactionsPreserveThePreviousFile` | Retains the detailed transaction scenario independently. |
| Incomplete analysis preserves existing baseline | `BaselinePersistenceTests.IncompleteAnalysisPreservesTheExistingBaseline` | Separate discovered test. |
| Baseline JSON boundary validation | `BaselinePersistenceTests.BaselineJsonBoundariesAreValidated` | Separate discovered test. |

The `RunSymlinkPackageAliasCase` and `RunDirectorySymlinkPackageAliasCase` helpers are case generators rather than independent assertions. Their direct-file, interior-directory, and ancestor-directory cases remain exercised by `FilesystemSafetyTests.ReachablePackageAliasesAreRejected`.

## Probe executable-harness scenarios

| Existing scenario | Discovered test or retained gate | Mapping notes |
| --- | --- | --- |
| Required fixture capabilities, target-framework context coverage, outer-target context and deduplication, present-versus-active state, and RID behavior | `scripts/run-phase0.ps1` invokes `tools/ProbeFixtureVerifier/ProbeFixtureVerifier.csproj` for each restored fixture | Retained end-to-end gate; each fixture has a distinct assets graph, so the verifier receives the fixture-specific assets path and required capability set. |
| Filesystem comparison-cache regression | Probe fixture verifier, invoked by Phase 0 for every fixture | The fixture graph is needed to exercise the original path-identity behavior. |
| Generated-import condition matrix, including TFM conditions, `Exists`, unsupported properties, nested context, and fail-closed result | Probe fixture verifier, invoked by Phase 0 | Keeps the original condition matrix and labels in the verifier's failure messages. |
| Generated-import file-identity agreement | Probe fixture verifier, invoked by Phase 0 | Retains the original fixture mutation and comparison. |
| Generated props/targets phase cross-wire and TFM-specific variants | Probe fixture verifier, invoked by Phase 0 | Retains the original phase and framework matrix. |
| Restore identity coherence | Probe fixture verifier, invoked by Phase 0 | Retains the original fixture-specific identity mutation. |
| Restore project identity reader | `ProbeModuleTests.RestoreMetadataReaderReadsTheProjectIdentity` | Direct internal-module seam test. |
| Reachable package-input path selection | `ProbeModuleTests.PackageGraphReaderReturnsOnlyReachablePackageInputs` | Direct internal-module seam test. |
| Generated-import target-framework condition parsing | `ProbeModuleTests.GeneratedImportConditionParserEvaluatesTargetFrameworks` | Direct internal-module seam test. |
| Build-transitive asset classification | `ProbeModuleTests.AssetClassifierMapsBuildTransitiveFiles` | Direct internal-module seam test. |
| Filesystem containment behavior | `ProbeModuleTests.FilesystemSafetyChecksContainmentAtTheModuleSeam` | Direct internal-module seam test. |
| Deterministic incomplete-reason normalization | `ProbeModuleTests.IncompleteAnalysisNormalizesReasonsDeterministically` | Direct internal-module seam test. |
| Valid empty graph, malformed restore documents, missing assets, traversal path, missing asset, and oversized restore input | `ProbeAnalysisTests` | Separate discovered cases; malformed documents are three theory data cases. |

## Retained repository gates

The repository gate continues to run controlled restore and Phase 0 fixtures, packageability audit, format and Release build checks, CLI documentation/help contracts, public text and no-code-execution checks, package and symbol inspection, isolated tool installation, deterministic baseline checks, PS007 fail-closed behavior, PowerShell adversarial controls, release-workflow contract checks, and vulnerability audit regressions. The solution's two test projects run via `dotnet test`; CI uploads both TRX result files on Windows, Linux, and macOS.
