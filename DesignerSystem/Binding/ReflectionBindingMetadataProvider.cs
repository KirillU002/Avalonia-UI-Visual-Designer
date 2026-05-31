using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace FormDesigner.DesignerSystem.Binding;

public sealed class ReflectionBindingMetadataProvider : IBindingMetadataProvider
{
    public string Id => "Reflection";

    private const string TableAttributeFullName = "System.Data.Linq.Mapping.TableAttribute";
    private const string ColumnAttributeFullName = "System.Data.Linq.Mapping.ColumnAttribute";
    private const string AssociationAttributeFullName = "System.Data.Linq.Mapping.AssociationAttribute";
    private const string TargetFrameworkAttributeFullName = "System.Runtime.Versioning.TargetFrameworkAttribute";

    public bool CanHandle(BindingImportRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.AssemblyPath)
            && string.Equals(System.IO.Path.GetExtension(request.AssemblyPath), ".dll", StringComparison.OrdinalIgnoreCase);
    }

    public BindingImportResult DiscoverSources(BindingImportRequest request)
    {
        Exception? portableMetadataFailure = null;

        try
        {
            return DiscoverSourcesFromPortableMetadata(request.AssemblyPath);
        }
        catch (Exception ex)
        {
            portableMetadataFailure = ex;
        }

        Exception? metadataLoadContextFailure = null;

        try
        {
            return DiscoverSourcesFromMetadata(request.AssemblyPath);
        }
        catch (Exception ex)
        {
            metadataLoadContextFailure = ex;
        }

        return DiscoverSourcesFromRuntime(request.AssemblyPath, portableMetadataFailure, metadataLoadContextFailure);
    }

    private BindingImportResult DiscoverSourcesFromPortableMetadata(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            throw new InvalidOperationException("Файл не содержит .NET metadata.");

        var reader = peReader.GetMetadataReader();
        var targetFrameworkName = ReadPortableTargetFrameworkName(reader);
        var failedMetadataReadCount = 0;
        var types = new List<PortableMetadataType>();
        foreach (var handle in reader.TypeDefinitions)
        {
            try
            {
                var type = ReadPortableMetadataType(reader, handle);
                if (!string.Equals(type.Name, "<Module>", StringComparison.Ordinal))
                    types.Add(type);
            }
            catch
            {
                failedMetadataReadCount++;
            }
        }

        var enumTypeNames = types
            .Where(type => type.IsEnum)
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);
        var dataContextEntityTypeNames = DiscoverDataContextTableEntityTypes(types);
        var analyses = types
            .Select(type => AnalyzePortableMetadataType(type, enumTypeNames, dataContextEntityTypeNames))
            .ToList();
        var hasMappedEntityTypes = analyses.Any(analysis => analysis.Type.HasTableAttribute
            || analysis.Type.HasColumnAttributes
            || dataContextEntityTypeNames.Contains(analysis.Type.FullName));
        var candidateAnalyses = analyses
            .Where(analysis => analysis.Category == ReflectionTypeCategory.Candidate)
            .Where(analysis => !hasMappedEntityTypes
                || analysis.Type.HasTableAttribute
                || analysis.Type.HasColumnAttributes
                || dataContextEntityTypeNames.Contains(analysis.Type.FullName))
            .OrderByDescending(analysis => analysis.Score)
            .ThenBy(analysis => analysis.Type.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<BindingSourceMetadata>();
        foreach (var analysis in candidateAnalyses)
        {
            try
            {
                results.Add(CreateBindingSourceFromPortableMetadata(analysis.Type, assemblyPath, targetFrameworkName, enumTypeNames));
            }
            catch
            {
                analysis.ImportFailed = true;
            }
        }

        return new BindingImportResult
        {
            Sources = results,
            Diagnostics = new BindingImportDiagnostics
            {
                ProviderId = Id,
                AssemblyPath = assemblyPath,
                ScannedTypeCount = analyses.Count,
                IgnoredTypeCount = analyses.Count(analysis => analysis.Category == ReflectionTypeCategory.Ignored),
                InfrastructureTypeCount = analyses.Count(analysis => analysis.Category == ReflectionTypeCategory.Infrastructure),
                CandidateTypeCount = candidateAnalyses.Count,
                ImportedSourceCount = results.Count,
                FailedCandidateTypeCount = candidateAnalyses.Count(analysis => analysis.ImportFailed),
                TableAttributedTypeCount = analyses.Count(analysis => analysis.Type.HasTableAttribute),
                ColumnAttributedTypeCount = analyses.Count(analysis => analysis.Type.HasColumnAttributes),
                LoaderExceptionCount = failedMetadataReadCount,
                CandidateTypeNames = candidateAnalyses
                    .Select(analysis => analysis.Type.FullName)
                    .Take(5)
                    .ToArray(),
                InfrastructureTypeNames = analyses
                    .Where(analysis => analysis.Category == ReflectionTypeCategory.Infrastructure)
                    .Select(analysis => analysis.Type.FullName)
                    .Take(5)
                    .ToArray()
            }
        };
    }

    private BindingImportResult DiscoverSourcesFromMetadata(string assemblyPath)
    {
        var resolverPaths = BuildMetadataResolverPaths(assemblyPath).ToList();
        Exception? firstFailure = null;
        var coreAssemblyNames = resolverPaths
            .Any(path => string.Equals(Path.GetFileNameWithoutExtension(path), "mscorlib", StringComparison.OrdinalIgnoreCase))
            ? new[] { "mscorlib", "System.Private.CoreLib" }
            : new[] { "System.Private.CoreLib", "mscorlib" };

        foreach (var coreAssemblyName in coreAssemblyNames)
        {
            try
            {
                using var metadataContext = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths), coreAssemblyName);
                var assembly = metadataContext.LoadFromAssemblyPath(assemblyPath);
                return DiscoverSourcesFromAssembly(assembly, assemblyPath);
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }

        throw new InvalidOperationException(
            "Не удалось прочитать DLL через fallback metadata loader. Основной DBML-импорт не требует зависимых DLL, но fallback для нестандартных сборок может потребовать дополнительные ссылки.",
            firstFailure);
    }

    private BindingImportResult DiscoverSourcesFromRuntime(string assemblyPath, Exception? portableMetadataFailure, Exception? metadataLoadContextFailure)
    {
        var loadContext = new DesignerAssemblyLoadContext(assemblyPath);

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            return DiscoverSourcesFromAssembly(assembly, assemblyPath, portableMetadataFailure ?? metadataLoadContextFailure);
        }
        catch (Exception ex)
        {
            var failureMessages = new List<string>();
            if (portableMetadataFailure is not null)
                failureMessages.Add($"Portable metadata: {portableMetadataFailure.GetBaseException().Message}");

            if (metadataLoadContextFailure is not null)
                failureMessages.Add($"MetadataLoadContext: {metadataLoadContextFailure.GetBaseException().Message}");

            failureMessages.Add($"Runtime: {ex.GetBaseException().Message}");

            return new BindingImportResult
            {
                Sources = Array.Empty<BindingSourceMetadata>(),
                Diagnostics = new BindingImportDiagnostics
                {
                    ProviderId = Id,
                    AssemblyPath = assemblyPath,
                    FailureMessage = string.Join(" | ", failureMessages)
                }
            };
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private BindingImportResult DiscoverSourcesFromAssembly(Assembly assembly, string assemblyPath, Exception? metadataFailure = null)
    {
        var results = new List<BindingSourceMetadata>();
        var loadableTypes = GetLoadableTypes(assembly);
        var analyses = loadableTypes.Types
            .Select(AnalyzeType)
            .ToList();
        var hasMappedEntityTypes = analyses.Any(analysis => analysis.HasTableAttribute || analysis.HasColumnAttributes);
        var candidateAnalyses = analyses
            .Where(analysis => analysis.Category == ReflectionTypeCategory.Candidate)
            .Where(analysis => !hasMappedEntityTypes || analysis.HasTableAttribute || analysis.HasColumnAttributes)
            .OrderByDescending(analysis => analysis.Score)
            .ThenBy(analysis => analysis.Type.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var analysis in candidateAnalyses)
        {
            try
            {
                results.Add(CreateBindingSourceFromType(analysis.Type, assemblyPath));
            }
            catch
            {
                // Skip problematic legacy types instead of failing the entire import.
                analysis.ImportFailed = true;
            }
        }

        var failureMessage = metadataFailure is null || results.Count > 0
            ? null
            : $"Metadata import failed, runtime fallback was used: {metadataFailure.GetBaseException().Message}";

        return new BindingImportResult
        {
            Sources = results,
            Diagnostics = new BindingImportDiagnostics
            {
                ProviderId = Id,
                AssemblyPath = assemblyPath,
                ScannedTypeCount = analyses.Count,
                IgnoredTypeCount = analyses.Count(analysis => analysis.Category == ReflectionTypeCategory.Ignored),
                InfrastructureTypeCount = analyses.Count(analysis => analysis.Category == ReflectionTypeCategory.Infrastructure),
                CandidateTypeCount = candidateAnalyses.Count,
                ImportedSourceCount = results.Count,
                FailedCandidateTypeCount = candidateAnalyses.Count(analysis => analysis.ImportFailed),
                TableAttributedTypeCount = analyses.Count(analysis => analysis.HasTableAttribute),
                ColumnAttributedTypeCount = analyses.Count(analysis => analysis.HasColumnAttributes),
                LoaderExceptionCount = loadableTypes.LoaderExceptionCount,
                CandidateTypeNames = candidateAnalyses
                    .Select(analysis => analysis.Type.FullName ?? analysis.Type.Name)
                    .Take(5)
                    .ToArray(),
                InfrastructureTypeNames = analyses
                    .Where(analysis => analysis.Category == ReflectionTypeCategory.Infrastructure)
                    .Select(analysis => analysis.Type.FullName ?? analysis.Type.Name)
                    .Take(5)
                    .ToArray(),
                FailureMessage = failureMessage ?? string.Empty
            }
        };
    }

    private static IEnumerable<string> BuildMetadataResolverPaths(string assemblyPath)
    {
        var paths = new List<string>();
        AddMetadataResolverPath(paths, assemblyPath);

        var assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            foreach (var path in EnumerateSafeAssemblyFiles(assemblyDirectory, SearchOption.AllDirectories))
                AddMetadataResolverPath(paths, path);
        }

        foreach (var path in EnumerateSafeAssemblyFiles(AppContext.BaseDirectory, SearchOption.TopDirectoryOnly))
            AddMetadataResolverPath(paths, path);

        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(assembly.Location))
                    AddMetadataResolverPath(paths, assembly.Location);
            }
            catch
            {
                // Some dynamic assemblies do not expose Location.
            }
        }

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                AddMetadataResolverPath(paths, path);
        }

        foreach (var path in EnumerateDotNetFrameworkReferenceAssemblies())
            AddMetadataResolverPath(paths, path);

        return paths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddMetadataResolverPath(ICollection<string> paths, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                paths.Add(fullPath);
        }
        catch
        {
            // Ignore malformed paths from legacy projects.
        }
    }

    private static IEnumerable<string> EnumerateSafeAssemblyFiles(string directory, SearchOption searchOption)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.dll", searchOption)
                .Concat(Directory.EnumerateFiles(directory, "*.exe", searchOption))
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
            yield return file;
    }

    private static IEnumerable<string> EnumerateDotNetFrameworkReferenceAssemblies()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrWhiteSpace(programFilesX86))
            yield break;

        var root = Path.Combine(programFilesX86, "Reference Assemblies", "Microsoft", "Framework", ".NETFramework");
        if (!Directory.Exists(root))
            yield break;

        IEnumerable<string> versionDirectories;
        try
        {
            versionDirectories = Directory.EnumerateDirectories(root)
                .OrderByDescending(directory => TryParseFrameworkVersion(Path.GetFileName(directory), out var version) ? version : new Version(0, 0))
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var directory in versionDirectories)
        {
            foreach (var file in EnumerateSafeAssemblyFiles(directory, SearchOption.AllDirectories))
                yield return file;
        }
    }

    private static bool TryParseFrameworkVersion(string? directoryName, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(directoryName))
            return false;

        if (Version.TryParse(directoryName.TrimStart('v', 'V'), out var parsedVersion))
        {
            version = parsedVersion;
            return true;
        }

        return false;
    }

    private static PortableMetadataType ReadPortableMetadataType(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var @namespace = reader.GetString(definition.Namespace);
        var fullName = string.IsNullOrWhiteSpace(@namespace) ? name : $"{@namespace}.{name}";
        var attributes = definition.Attributes;
        var customAttributes = definition.GetCustomAttributes();
        var properties = new List<PortableMetadataProperty>();
        foreach (var propertyHandle in definition.GetProperties())
        {
            try
            {
                properties.Add(ReadPortableMetadataProperty(reader, propertyHandle));
            }
            catch
            {
                // A single malformed/external property must not break DBML import.
            }
        }

        return new PortableMetadataType
        {
            Name = name,
            Namespace = @namespace,
            FullName = fullName,
            BaseTypeFullName = GetEntityFullName(reader, definition.BaseType),
            IsClass = (attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Class
                && (attributes & TypeAttributes.Interface) == 0,
            IsAbstract = (attributes & TypeAttributes.Abstract) != 0,
            IsGenericTypeDefinition = definition.GetGenericParameters().Count > 0,
            IsNested = !definition.GetDeclaringType().IsNil,
            IsEnum = string.Equals(GetEntityFullName(reader, definition.BaseType), "System.Enum", StringComparison.Ordinal),
            HasTableAttribute = HasPortableAttribute(reader, customAttributes, TableAttributeFullName),
            TableName = ReadPortableNamedStringAttributeValue(reader, customAttributes, TableAttributeFullName, "Name"),
            Properties = properties
        };
    }

    private static PortableMetadataProperty ReadPortableMetadataProperty(MetadataReader reader, PropertyDefinitionHandle handle)
    {
        var definition = reader.GetPropertyDefinition(handle);
        var name = reader.GetString(definition.Name);
        var accessors = definition.GetAccessors();
        var isPublicReadable = false;
        if (!accessors.Getter.IsNil)
        {
            var getter = reader.GetMethodDefinition(accessors.Getter);
            isPublicReadable = (getter.Attributes & MethodAttributes.Public) != 0
                && (getter.Attributes & MethodAttributes.Static) == 0;
        }

        var typeName = "object";
        var isIndexer = false;
        try
        {
            var signature = definition.DecodeSignature(PortableMetadataSignatureTypeProvider.Instance, genericContext: null);
            typeName = signature.ReturnType;
            isIndexer = signature.RequiredParameterCount > 0 || signature.ParameterTypes.Length > 0;
        }
        catch
        {
            typeName = "object";
        }

        var customAttributes = definition.GetCustomAttributes();
        return new PortableMetadataProperty
        {
            Name = name,
            TypeName = NormalizePortableTypeName(typeName),
            IsPublicReadable = isPublicReadable && !isIndexer,
            HasColumnAttribute = HasPortableAttribute(reader, customAttributes, ColumnAttributeFullName),
            HasAssociationAttribute = HasPortableAttribute(reader, customAttributes, AssociationAttributeFullName)
        };
    }

    private static bool HasPortableAttribute(MetadataReader reader, CustomAttributeHandleCollection handles, string attributeFullName)
    {
        foreach (var handle in handles)
        {
            if (string.Equals(GetPortableAttributeTypeFullName(reader, handle), attributeFullName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string ReadPortableTargetFrameworkName(MetadataReader reader)
    {
        try
        {
            return ReadPortableFirstStringAttributeValue(
                reader,
                reader.GetAssemblyDefinition().GetCustomAttributes(),
                TargetFrameworkAttributeFullName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadPortableFirstStringAttributeValue(MetadataReader reader, CustomAttributeHandleCollection handles, string attributeFullName)
    {
        foreach (var handle in handles)
        {
            if (!string.Equals(GetPortableAttributeTypeFullName(reader, handle), attributeFullName, StringComparison.Ordinal))
                continue;

            try
            {
                var blobReader = reader.GetBlobReader(reader.GetCustomAttribute(handle).Value);
                if (blobReader.ReadUInt16() != 1)
                    return string.Empty;

                return ReadPortableSerializedString(ref blobReader);
            }
            catch
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    private static string ReadPortableNamedStringAttributeValue(
        MetadataReader reader,
        CustomAttributeHandleCollection handles,
        string attributeFullName,
        string memberName)
    {
        foreach (var handle in handles)
        {
            if (!string.Equals(GetPortableAttributeTypeFullName(reader, handle), attributeFullName, StringComparison.Ordinal))
                continue;

            try
            {
                var blobReader = reader.GetBlobReader(reader.GetCustomAttribute(handle).Value);
                if (blobReader.ReadUInt16() != 1 || blobReader.RemainingBytes < 2)
                    return string.Empty;

                var namedArgumentCount = blobReader.ReadUInt16();
                for (var index = 0; index < namedArgumentCount && blobReader.RemainingBytes > 0; index++)
                {
                    _ = blobReader.ReadByte(); // FIELD or PROPERTY marker.
                    var fieldOrPropertyType = blobReader.ReadByte();
                    if (fieldOrPropertyType != 0x0E) // ELEMENT_TYPE_STRING
                        return string.Empty;

                    var currentMemberName = ReadPortableSerializedString(ref blobReader);
                    var value = ReadPortableSerializedString(ref blobReader);
                    if (string.Equals(currentMemberName, memberName, StringComparison.Ordinal))
                        return value;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    private static string ReadPortableSerializedString(ref BlobReader blobReader)
    {
        if (blobReader.RemainingBytes <= 0)
            return string.Empty;

        var firstByte = blobReader.ReadByte();
        if (firstByte == 0xFF)
            return string.Empty;

        int length;
        if ((firstByte & 0x80) == 0)
        {
            length = firstByte;
        }
        else if ((firstByte & 0xC0) == 0x80)
        {
            length = ((firstByte & 0x3F) << 8) | blobReader.ReadByte();
        }
        else
        {
            length = ((firstByte & 0x1F) << 24)
                | (blobReader.ReadByte() << 16)
                | (blobReader.ReadByte() << 8)
                | blobReader.ReadByte();
        }

        return length <= 0 || length > blobReader.RemainingBytes
            ? string.Empty
            : blobReader.ReadUTF8(length);
    }

    private static string GetPortableAttributeTypeFullName(MetadataReader reader, CustomAttributeHandle handle)
    {
        try
        {
            var attribute = reader.GetCustomAttribute(handle);
            return attribute.Constructor.Kind switch
            {
                HandleKind.MemberReference => GetMemberReferenceDeclaringTypeFullName(reader, (MemberReferenceHandle)attribute.Constructor),
                HandleKind.MethodDefinition => GetMethodDefinitionDeclaringTypeFullName(reader, (MethodDefinitionHandle)attribute.Constructor),
                _ => string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetMemberReferenceDeclaringTypeFullName(MetadataReader reader, MemberReferenceHandle handle)
    {
        var memberReference = reader.GetMemberReference(handle);
        return GetEntityFullName(reader, memberReference.Parent);
    }

    private static string GetMethodDefinitionDeclaringTypeFullName(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var methodDefinition = reader.GetMethodDefinition(handle);
        return GetEntityFullName(reader, methodDefinition.GetDeclaringType());
    }

    private static string GetEntityFullName(MetadataReader reader, EntityHandle handle)
    {
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => GetTypeDefinitionFullName(reader, (TypeDefinitionHandle)handle),
                HandleKind.TypeReference => GetTypeReferenceFullName(reader, (TypeReferenceHandle)handle),
                HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                    .DecodeSignature(PortableMetadataSignatureTypeProvider.Instance, genericContext: null),
                _ => string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetTypeDefinitionFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var @namespace = reader.GetString(definition.Namespace);
        return string.IsNullOrWhiteSpace(@namespace) ? name : $"{@namespace}.{name}";
    }

    private static string GetTypeReferenceFullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        var @namespace = reader.GetString(reference.Namespace);
        return string.IsNullOrWhiteSpace(@namespace) ? name : $"{@namespace}.{name}";
    }

    private static PortableMetadataTypeAnalysis AnalyzePortableMetadataType(
        PortableMetadataType type,
        IReadOnlySet<string> enumTypeNames,
        IReadOnlySet<string> dataContextEntityTypeNames)
    {
        if (!IsCandidatePortableEntityType(type))
        {
            return new PortableMetadataTypeAnalysis
            {
                Type = type,
                Category = ReflectionTypeCategory.Ignored,
                Score = 0
            };
        }

        if (LooksLikePortableInfrastructureType(type))
        {
            return new PortableMetadataTypeAnalysis
            {
                Type = type,
                Category = ReflectionTypeCategory.Infrastructure,
                Score = 0
            };
        }

        var score = GetPortableBindingEntityScore(type, enumTypeNames, dataContextEntityTypeNames);
        return new PortableMetadataTypeAnalysis
        {
            Type = type,
            Category = score > 0 ? ReflectionTypeCategory.Candidate : ReflectionTypeCategory.Ignored,
            Score = score
        };
    }

    private static bool IsCandidatePortableEntityType(PortableMetadataType type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition || type.IsNested)
            return false;

        if (string.Equals(type.BaseTypeFullName, "System.Delegate", StringComparison.Ordinal)
            || string.Equals(type.BaseTypeFullName, "System.MulticastDelegate", StringComparison.Ordinal)
            || string.Equals(type.BaseTypeFullName, "System.Attribute", StringComparison.Ordinal)
            || string.Equals(type.BaseTypeFullName, "System.Exception", StringComparison.Ordinal))
        {
            return false;
        }

        if (type.FullName.StartsWith("<>", StringComparison.Ordinal)
            || type.FullName.Contains("AnonymousType", StringComparison.Ordinal)
            || type.FullName.Contains("DisplayClass", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikePortableInfrastructureType(PortableMetadataType type)
    {
        var name = type.Name;
        if (name.EndsWith("Context", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("DataContext", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Service", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Provider", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ViewModel", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(type.FullName, "System.Data.Linq.DataContext", StringComparison.Ordinal)
            || string.Equals(type.BaseTypeFullName, "System.Data.Linq.DataContext", StringComparison.Ordinal);
    }

    private static int GetPortableBindingEntityScore(
        PortableMetadataType type,
        IReadOnlySet<string> enumTypeNames,
        IReadOnlySet<string> dataContextEntityTypeNames)
    {
        var scalarProperties = type.Properties
            .Where(property => IsBindablePortableProperty(property, enumTypeNames))
            .ToList();
        var columnCount = type.Properties.Count(property => property.HasColumnAttribute);
        var isDataContextTableEntity = dataContextEntityTypeNames.Contains(type.FullName);

        if (!type.HasTableAttribute && columnCount == 0 && !isDataContextTableEntity && scalarProperties.Count < 2)
            return 0;

        var score = 0;
        if (type.HasTableAttribute || isDataContextTableEntity)
            score += 100;

        if (columnCount > 0)
            score += 40 + Math.Min(columnCount * 5, 40);

        score += Math.Min(scalarProperties.Count * 4, 32);

        if (LooksLikePortableInfrastructureType(type))
            score -= 90;

        return score > 0 ? score : 0;
    }

    private static BindingSourceMetadata CreateBindingSourceFromPortableMetadata(
        PortableMetadataType type,
        string assemblyPath,
        string targetFrameworkName,
        IReadOnlySet<string> enumTypeNames)
    {
        var baseName = type.Name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? type.Name : $"{type.Name}s";
        var targetFrameworkSuffix = string.IsNullOrWhiteSpace(targetFrameworkName)
            ? string.Empty
            : $" ({targetFrameworkName})";
        return new BindingSourceMetadata
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = baseName,
            Path = baseName,
            ItemTypeName = type.Name,
            Description = $"Импортировано из {Path.GetFileName(assemblyPath)}{targetFrameworkSuffix}",
            SourceKind = "Assembly",
            SourceAssemblyPath = assemblyPath,
            SourceTypeFullName = type.FullName,
            SourceTableName = type.TableName,
            Fields = type.Properties
                .Where(property => IsBindablePortableProperty(property, enumTypeNames))
                .OrderBy(property => property.Name)
                .Select(property => CreateBindingFieldFromPortableMetadataProperty(property, enumTypeNames))
                .ToList()
        };
    }

    private static BindingFieldMetadata CreateBindingFieldFromPortableMetadataProperty(PortableMetadataProperty property, IReadOnlySet<string> enumTypeNames)
    {
        return new BindingFieldMetadata
        {
            Header = property.Name,
            Path = property.Name,
            SampleValue = GetPortableSampleValue(property.TypeName, enumTypeNames),
            Width = IsCompactPortableColumnType(property.TypeName, enumTypeNames) ? "120" : "*",
            TypeName = GetPortableFriendlyTypeName(property.TypeName),
            IsVisible = true,
            IsSortable = IsSortablePortableType(property.TypeName, enumTypeNames),
            SortDirection = BindingFieldModel.SortDirectionNone,
            SortOrder = -1,
            GroupOrder = -1
        };
    }

    private static HashSet<string> DiscoverDataContextTableEntityTypes(IReadOnlyList<PortableMetadataType> types)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types.Where(LooksLikePortableInfrastructureType))
        {
            foreach (var property in type.Properties.Where(property => property.IsPublicReadable))
            {
                var entityTypeName = TryExtractPortableGenericArgument(property.TypeName, "System.Data.Linq.Table`1");
                if (!string.IsNullOrWhiteSpace(entityTypeName))
                    result.Add(entityTypeName);
            }
        }

        return result;
    }

    private static bool IsBindablePortableProperty(PortableMetadataProperty property, IReadOnlySet<string> enumTypeNames)
    {
        if (!property.IsPublicReadable || property.HasAssociationAttribute || IsPortableRelationshipType(property.TypeName))
            return false;

        return IsScalarPortableType(property.TypeName, enumTypeNames)
            || property.HasColumnAttribute;
    }

    private static bool IsPortableRelationshipType(string typeName)
    {
        typeName = NormalizePortableTypeName(typeName);
        return typeName.StartsWith("System.Data.Linq.EntitySet`1<", StringComparison.Ordinal)
            || typeName.StartsWith("System.Data.Linq.EntityRef`1<", StringComparison.Ordinal)
            || typeName.StartsWith("System.Collections.Generic.ICollection`1<", StringComparison.Ordinal)
            || typeName.StartsWith("System.Collections.Generic.IEnumerable`1<", StringComparison.Ordinal)
            || typeName.StartsWith("System.Collections.Generic.IList`1<", StringComparison.Ordinal)
            || typeName.StartsWith("System.Collections.Generic.List`1<", StringComparison.Ordinal);
    }

    private static string TryExtractPortableGenericArgument(string typeName, string genericTypeName)
    {
        typeName = NormalizePortableTypeName(typeName);
        var prefix = $"{genericTypeName}<";
        if (!typeName.StartsWith(prefix, StringComparison.Ordinal) || !typeName.EndsWith(">", StringComparison.Ordinal))
            return string.Empty;

        return typeName.Substring(prefix.Length, typeName.Length - prefix.Length - 1).Trim();
    }

    private static string NormalizePortableTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return "System.Object";

        return typeName switch
        {
            "bool" => "System.Boolean",
            "byte" => "System.Byte",
            "sbyte" => "System.SByte",
            "short" => "System.Int16",
            "ushort" => "System.UInt16",
            "int" => "System.Int32",
            "uint" => "System.UInt32",
            "long" => "System.Int64",
            "ulong" => "System.UInt64",
            "float" => "System.Single",
            "double" => "System.Double",
            "decimal" => "System.Decimal",
            "char" => "System.Char",
            "string" => "System.String",
            "object" => "System.Object",
            _ => typeName
        };
    }

    private static bool IsScalarPortableType(string typeName, IReadOnlySet<string> enumTypeNames)
    {
        typeName = NormalizePortableTypeName(typeName);
        if (enumTypeNames.Contains(typeName))
            return true;

        return typeName is "System.Boolean"
            or "System.Byte"
            or "System.SByte"
            or "System.Int16"
            or "System.UInt16"
            or "System.Int32"
            or "System.UInt32"
            or "System.Int64"
            or "System.UInt64"
            or "System.Single"
            or "System.Double"
            or "System.Decimal"
            or "System.Char"
            or "System.String"
            or "System.DateTime"
            or "System.DateTimeOffset"
            or "System.TimeSpan"
            or "System.Guid"
            or "System.Byte[]"
            or "byte[]"
            or "System.Data.Linq.Binary";
    }

    private static bool IsSortablePortableType(string typeName, IReadOnlySet<string> enumTypeNames)
    {
        typeName = NormalizePortableTypeName(typeName);
        return IsScalarPortableType(typeName, enumTypeNames)
            && typeName is not "System.Byte[]"
            && typeName is not "byte[]"
            && typeName is not "System.Data.Linq.Binary";
    }

    private static bool IsCompactPortableColumnType(string typeName, IReadOnlySet<string> enumTypeNames)
    {
        typeName = NormalizePortableTypeName(typeName);
        if (enumTypeNames.Contains(typeName))
            return true;

        return typeName is "System.Boolean"
            or "System.Int16"
            or "System.UInt16"
            or "System.Int32"
            or "System.UInt32"
            or "System.Int64"
            or "System.UInt64"
            or "System.Single"
            or "System.Double"
            or "System.Decimal"
            or "System.DateTime"
            or "System.DateTimeOffset"
            or "System.TimeSpan"
            or "System.Guid";
    }

    private static string GetPortableSampleValue(string typeName, IReadOnlySet<string> enumTypeNames)
    {
        typeName = NormalizePortableTypeName(typeName);
        if (enumTypeNames.Contains(typeName))
            return "Value";

        return typeName switch
        {
            "System.String" => "Текст",
            "System.Boolean" => "True",
            "System.DateTime" or "System.DateTimeOffset" => "2026-04-12",
            "System.TimeSpan" => "01:30:00",
            "System.Guid" => "3F2504E0-4F89-41D3-9A0C-0305E82C3301",
            "System.Decimal" or "System.Double" or "System.Single" => "123.45",
            "System.Byte[]" or "byte[]" or "System.Data.Linq.Binary" => "<данные>",
            "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" => "1",
            _ => "Value"
        };
    }

    private static string GetPortableFriendlyTypeName(string typeName)
    {
        typeName = NormalizePortableTypeName(typeName);
        return typeName switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Decimal" => "decimal",
            "System.Char" => "char",
            "System.String" => "string",
            "System.DateTime" => "DateTime",
            "System.DateTimeOffset" => "DateTimeOffset",
            "System.TimeSpan" => "TimeSpan",
            "System.Guid" => "Guid",
            "System.Byte[]" or "byte[]" or "System.Data.Linq.Binary" => "byte[]",
            _ => typeName.Split('.').LastOrDefault() ?? typeName
        };
    }

    private static BindingSourceMetadata CreateBindingSourceFromType(Type type, string assemblyPath)
    {
        var tableName = GetTableName(type);
        var baseName = type.Name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? type.Name : $"{type.Name}s";
        return new BindingSourceMetadata
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = baseName,
            Path = baseName,
            ItemTypeName = type.Name,
            Description = $"Импортировано из {System.IO.Path.GetFileName(assemblyPath)}",
            SourceKind = "Assembly",
            SourceAssemblyPath = assemblyPath,
            SourceTypeFullName = type.FullName ?? type.Name,
            SourceTableName = tableName,
            Fields = GetBindableProperties(type)
                .Select(CreateBindingFieldFromProperty)
                .ToList()
        };
    }

    private static IEnumerable<PropertyInfo> GetBindableProperties(Type type)
    {
        return GetPublicInstanceProperties(type)
            .Where(CanReadPropertySafely)
            .Where(property => !HasAttribute(property, AssociationAttributeFullName))
            .Where(IsBindableScalarProperty)
            .OrderBy(property => property.Name);
    }

    private static int GetBindingEntityScore(Type type)
    {
        if (!IsCandidateEntityType(type))
            return 0;

        var properties = GetPublicInstanceProperties(type);
        var scalarProperties = properties
            .Where(CanReadPropertySafely)
            .Where(property => !HasAttribute(property, AssociationAttributeFullName))
            .Where(IsBindableScalarProperty)
            .ToList();
        var scalarPropertyCount = scalarProperties.Count;
        var hasTableAttribute = HasAttribute(type, TableAttributeFullName);
        var columnCount = properties.Count(property => HasAttribute(property, ColumnAttributeFullName));

        if (!hasTableAttribute && columnCount == 0 && scalarPropertyCount < 2)
            return 0;

        var score = 0;
        if (hasTableAttribute)
            score += 100;

        if (columnCount > 0)
            score += 40 + Math.Min(columnCount * 5, 40);

        score += Math.Min(scalarPropertyCount * 4, 32);

        if (!string.IsNullOrWhiteSpace(GetTableName(type)))
            score += 10;

        if (LooksLikeInfrastructureType(type))
            score -= 90;

        return score > 0 ? score : 0;
    }

    private static ReflectionTypeAnalysis AnalyzeType(Type type)
    {
        if (!IsCandidateEntityType(type))
        {
            return new ReflectionTypeAnalysis
            {
                Type = type,
                Category = ReflectionTypeCategory.Ignored,
                Score = 0,
                HasTableAttribute = HasAttribute(type, TableAttributeFullName),
                HasColumnAttributes = GetPublicInstanceProperties(type).Any(property => HasAttribute(property, ColumnAttributeFullName))
            };
        }

        if (LooksLikeInfrastructureType(type))
        {
            return new ReflectionTypeAnalysis
            {
                Type = type,
                Category = ReflectionTypeCategory.Infrastructure,
                Score = 0,
                HasTableAttribute = HasAttribute(type, TableAttributeFullName),
                HasColumnAttributes = GetPublicInstanceProperties(type).Any(property => HasAttribute(property, ColumnAttributeFullName))
            };
        }

        var score = GetBindingEntityScore(type);
        return new ReflectionTypeAnalysis
        {
            Type = type,
            Category = score > 0 ? ReflectionTypeCategory.Candidate : ReflectionTypeCategory.Ignored,
            Score = score,
            HasTableAttribute = HasAttribute(type, TableAttributeFullName),
            HasColumnAttributes = GetPublicInstanceProperties(type).Any(property => HasAttribute(property, ColumnAttributeFullName))
        };
    }

    private static bool IsCandidateEntityType(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition || type.IsNested)
            return false;

        if (IsAssignableToByFullName(type, "System.Delegate")
            || IsAssignableToByFullName(type, "System.MulticastDelegate"))
            return false;

        if (IsAssignableToByFullName(type, "System.Attribute")
            || IsAssignableToByFullName(type, "System.Exception"))
            return false;

        if (type.FullName is not null
            && (type.FullName.StartsWith("<>", StringComparison.Ordinal)
                || type.FullName.Contains("AnonymousType", StringComparison.Ordinal)
                || type.FullName.Contains("DisplayClass", StringComparison.Ordinal)))
        {
            return false;
        }

        return true;
    }

    private static string GetTableName(MemberInfo typeOrProperty)
    {
        var tableAttribute = SafeGetCustomAttributes(typeOrProperty)
            .FirstOrDefault(attribute => string.Equals(attribute.AttributeType.FullName, TableAttributeFullName, StringComparison.Ordinal));

        if (tableAttribute is null)
            return string.Empty;

        var namedArgument = tableAttribute.NamedArguments.FirstOrDefault(argument => string.Equals(argument.MemberName, "Name", StringComparison.Ordinal));
        if (namedArgument.TypedValue.Value is string namedValue && !string.IsNullOrWhiteSpace(namedValue))
            return namedValue;

        if (tableAttribute.ConstructorArguments.Count > 0
            && tableAttribute.ConstructorArguments[0].Value is string constructorValue
            && !string.IsNullOrWhiteSpace(constructorValue))
        {
            return constructorValue;
        }

        return string.Empty;
    }

    private static BindingFieldMetadata CreateBindingFieldFromProperty(PropertyInfo property)
    {
        var propertyType = GetBindablePropertyType(property);
        return new BindingFieldMetadata
        {
            Header = property.Name,
            Path = property.Name,
            SampleValue = GetSampleValue(propertyType),
            Width = IsCompactColumnType(propertyType) ? "120" : "*",
            TypeName = GetFriendlyTypeName(propertyType),
            IsVisible = true,
            IsSortable = IsSortablePropertyType(propertyType),
            SortDirection = BindingFieldModel.SortDirectionNone,
            SortOrder = -1,
            GroupOrder = -1
        };
    }

    private static bool LooksLikeInfrastructureType(Type type)
    {
        var fullName = type.FullName ?? string.Empty;
        var name = type.Name;

        if (name.EndsWith("Context", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("DataContext", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Service", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Provider", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ViewModel", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(fullName, "System.Data.Linq.DataContext", StringComparison.Ordinal)
            || IsAssignableToByFullName(type, "System.Data.Linq.DataContext"))
        {
            return true;
        }

        return false;
    }

    private static bool IsAssignableToByFullName(Type type, string targetFullName)
    {
        for (var current = type; current is not null;)
        {
            if (string.Equals(current.FullName, targetFullName, StringComparison.Ordinal))
                return true;

            try
            {
                current = current.BaseType;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static LoadableTypeSnapshot GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return new LoadableTypeSnapshot
            {
                Types = assembly.GetTypes(),
                LoaderExceptionCount = 0
            };
        }
        catch (ReflectionTypeLoadException ex)
        {
            return new LoadableTypeSnapshot
            {
                Types = ex.Types.Where(type => type is not null).Cast<Type>().ToArray(),
                LoaderExceptionCount = ex.LoaderExceptions?.Length ?? 0
            };
        }
        catch
        {
            return new LoadableTypeSnapshot
            {
                Types = Array.Empty<Type>(),
                LoaderExceptionCount = 0
            };
        }
    }

    private static IReadOnlyList<PropertyInfo> GetPublicInstanceProperties(Type type)
    {
        try
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }
        catch
        {
            return Array.Empty<PropertyInfo>();
        }
    }

    private static IEnumerable<CustomAttributeData> SafeGetCustomAttributes(MemberInfo member)
    {
        try
        {
            return CustomAttributeData.GetCustomAttributes(member);
        }
        catch
        {
            return Array.Empty<CustomAttributeData>();
        }
    }

    private static bool HasAttribute(MemberInfo member, string attributeFullName)
    {
        return SafeGetCustomAttributes(member)
            .Any(attribute => string.Equals(attribute.AttributeType.FullName, attributeFullName, StringComparison.Ordinal));
    }

    private static bool CanReadPropertySafely(PropertyInfo property)
    {
        try
        {
            return property.CanRead && property.GetMethod?.GetParameters().Length == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBindableScalarProperty(PropertyInfo property)
    {
        try
        {
            return IsScalarPropertyType(GetBindablePropertyType(property));
        }
        catch
        {
            return false;
        }
    }

    private static Type GetBindablePropertyType(PropertyInfo property)
    {
        return GetEffectivePropertyType(property.PropertyType);
    }

    private static bool IsScalarPropertyType(Type type)
    {
        type = GetEffectivePropertyType(type);
        var fullName = type.FullName;
/*
        if (string.Equals(fullName, "System.String", StringComparison.Ordinal)) return "Текст";
        if (string.Equals(fullName, "System.Boolean", StringComparison.Ordinal)) return "True";
        if (string.Equals(fullName, "System.DateTime", StringComparison.Ordinal)
            || string.Equals(fullName, "System.DateTimeOffset", StringComparison.Ordinal)) return "2026-04-12";
        if (string.Equals(fullName, "System.TimeSpan", StringComparison.Ordinal)) return "01:30:00";
        if (string.Equals(fullName, "System.Guid", StringComparison.Ordinal)) return "3F2504E0-4F89-41D3-9A0C-0305E82C3301";
        if (string.Equals(fullName, "System.Decimal", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Double", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Single", StringComparison.Ordinal)) return "123.45";
        if (IsBinaryLikeType(type)) return "<данные>";
        if (string.Equals(fullName, "System.Int16", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Int32", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Int64", StringComparison.Ordinal)) return "1";
        if (type.IsEnum) return GetFirstEnumFieldName(type);

*/
        return type.IsEnum
            || string.Equals(fullName, "System.String", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Decimal", StringComparison.Ordinal)
            || string.Equals(fullName, "System.DateTime", StringComparison.Ordinal)
            || string.Equals(fullName, "System.DateTimeOffset", StringComparison.Ordinal)
            || string.Equals(fullName, "System.TimeSpan", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Guid", StringComparison.Ordinal)
            || IsBinaryLikeType(type)
            || type.IsPrimitive;
    }

    private static bool IsSortablePropertyType(Type type)
    {
        type = GetEffectivePropertyType(type);
        var fullName = type.FullName;

        return type.IsEnum
            || type.IsPrimitive
            || string.Equals(fullName, "System.Decimal", StringComparison.Ordinal)
            || string.Equals(fullName, "System.DateTime", StringComparison.Ordinal)
            || string.Equals(fullName, "System.DateTimeOffset", StringComparison.Ordinal)
            || string.Equals(fullName, "System.TimeSpan", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Guid", StringComparison.Ordinal)
            || string.Equals(fullName, "System.String", StringComparison.Ordinal);
    }

    private static bool IsCompactColumnType(Type type)
    {
        type = GetEffectivePropertyType(type);
        var fullName = type.FullName;

        return string.Equals(fullName, "System.Boolean", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Int16", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Int32", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Int64", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Single", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Double", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Decimal", StringComparison.Ordinal)
            || string.Equals(fullName, "System.DateTime", StringComparison.Ordinal)
            || string.Equals(fullName, "System.DateTimeOffset", StringComparison.Ordinal)
            || string.Equals(fullName, "System.TimeSpan", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Guid", StringComparison.Ordinal);
    }

    private static string GetSampleValue(Type type)
    {
        type = GetEffectivePropertyType(type);
        var fullName = type.FullName;

        if (type == typeof(string)) return "Текст";
        if (string.Equals(fullName, "System.String", StringComparison.Ordinal)) return "Текст";
        if (string.Equals(fullName, "System.Boolean", StringComparison.Ordinal)) return "True";
        if (string.Equals(fullName, "System.DateTime", StringComparison.Ordinal)
            || string.Equals(fullName, "System.DateTimeOffset", StringComparison.Ordinal)) return "2026-04-12";
        if (string.Equals(fullName, "System.TimeSpan", StringComparison.Ordinal)) return "01:30:00";
        if (string.Equals(fullName, "System.Guid", StringComparison.Ordinal)) return "3F2504E0-4F89-41D3-9A0C-0305E82C3301";
        if (string.Equals(fullName, "System.Decimal", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Double", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Single", StringComparison.Ordinal)) return "123.45";
        if (IsBinaryLikeType(type)) return "<данные>";
        if (string.Equals(fullName, "System.Int16", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Int32", StringComparison.Ordinal)
            || string.Equals(fullName, "System.Int64", StringComparison.Ordinal)) return "1";
        if (type.IsEnum) return GetFirstEnumFieldName(type);

        if (type == typeof(bool)) return "True";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "2026-04-12";
        if (type == typeof(TimeSpan)) return "01:30:00";
        if (type == typeof(Guid)) return "3F2504E0-4F89-41D3-9A0C-0305E82C3301";
        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float)) return "123.45";
        if (type == typeof(byte[])) return "<данные>";
        if (type == typeof(short) || type == typeof(int) || type == typeof(long)) return "1";
        if (type.IsEnum) return Enum.GetNames(type).FirstOrDefault() ?? "Value";

        return "Value";
    }

    private static string GetFriendlyTypeName(Type type)
    {
        type = GetEffectivePropertyType(type);
        var fullName = type.FullName;

        if (string.Equals(fullName, "System.Int32", StringComparison.Ordinal)) return "int";
        if (string.Equals(fullName, "System.Int64", StringComparison.Ordinal)) return "long";
        if (string.Equals(fullName, "System.Int16", StringComparison.Ordinal)) return "short";
        if (string.Equals(fullName, "System.Decimal", StringComparison.Ordinal)) return "decimal";
        if (string.Equals(fullName, "System.Double", StringComparison.Ordinal)) return "double";
        if (string.Equals(fullName, "System.Single", StringComparison.Ordinal)) return "float";
        if (string.Equals(fullName, "System.Boolean", StringComparison.Ordinal)) return "bool";
        if (string.Equals(fullName, "System.String", StringComparison.Ordinal)) return "string";
        if (string.Equals(fullName, "System.DateTime", StringComparison.Ordinal)) return "DateTime";
        if (string.Equals(fullName, "System.DateTimeOffset", StringComparison.Ordinal)) return "DateTimeOffset";
        if (string.Equals(fullName, "System.TimeSpan", StringComparison.Ordinal)) return "TimeSpan";
        if (string.Equals(fullName, "System.Guid", StringComparison.Ordinal)) return "Guid";
        if (IsBinaryLikeType(type)) return "byte[]";

        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(short)) return "short";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(double)) return "double";
        if (type == typeof(float)) return "float";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(string)) return "string";
        if (type == typeof(DateTime)) return "DateTime";
        if (type == typeof(DateTimeOffset)) return "DateTimeOffset";
        if (type == typeof(TimeSpan)) return "TimeSpan";
        if (type == typeof(Guid)) return "Guid";
        if (type == typeof(byte[])) return "byte[]";
        return type.Name;
    }

    private static Type GetEffectivePropertyType(Type type)
    {
        try
        {
            if (type.IsGenericType
                && string.Equals(type.GetGenericTypeDefinition().FullName, "System.Nullable`1", StringComparison.Ordinal))
            {
                return type.GetGenericArguments()[0];
            }
        }
        catch
        {
            // Metadata-only types may fail generic inspection when an optional dependency is missing.
        }

        return type;
    }

    private static bool IsBinaryLikeType(Type type)
    {
        type = GetEffectivePropertyType(type);

        if (string.Equals(type.FullName, "System.Data.Linq.Binary", StringComparison.Ordinal))
            return true;

        if (!type.IsArray)
            return false;

        try
        {
            return string.Equals(type.GetElementType()?.FullName, "System.Byte", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string GetFirstEnumFieldName(Type type)
    {
        try
        {
            return type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => field.Name)
                .FirstOrDefault() ?? "Value";
        }
        catch
        {
            return "Value";
        }
    }

    private sealed class PortableMetadataSignatureTypeProvider : ISignatureTypeProvider<string, object?>
    {
        public static PortableMetadataSignatureTypeProvider Instance { get; } = new();

        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[]";

        public string GetByReferenceType(string elementType) => elementType;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "System.IntPtr";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            if (string.Equals(genericType, "System.Nullable`1", StringComparison.Ordinal)
                && typeArguments.Length == 1)
            {
                return typeArguments[0];
            }

            return $"{genericType}<{string.Join(", ", typeArguments)}>";
        }

        public string GetGenericMethodParameter(object? genericContext, int index) => $"TMethod{index}";

        public string GetGenericTypeParameter(object? genericContext, int index) => $"T{index}";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;

        public string GetPointerType(string elementType) => elementType;

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return typeCode switch
            {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.Void => "System.Void",
                _ => typeCode.ToString()
            };
        }

        public string GetSZArrayType(string elementType)
        {
            return string.Equals(elementType, "System.Byte", StringComparison.Ordinal)
                ? "System.Byte[]"
                : $"{elementType}[]";
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            return GetTypeDefinitionFullName(reader, handle);
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            return GetTypeReferenceFullName(reader, handle);
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }
    }

    private sealed class DesignerAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly Dictionary<string, Assembly> _sharedAssemblies;
        private readonly Dictionary<string, string> _probeAssemblyPaths;

        public DesignerAssemblyLoadContext(string assemblyPath)
            : base($"BindingImport:{System.IO.Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(assemblyPath);
            _sharedAssemblies = AssemblyLoadContext.Default.Assemblies
                .Where(assembly => !string.IsNullOrWhiteSpace(assembly.GetName().Name))
                .GroupBy(assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            _probeAssemblyPaths = BuildProbeAssemblyPaths(assemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (!string.IsNullOrWhiteSpace(assemblyName.Name)
                && _sharedAssemblies.TryGetValue(assemblyName.Name, out var sharedAssembly))
            {
                return sharedAssembly;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is not null)
                return LoadFromAssemblyPath(path);

            if (!string.IsNullOrWhiteSpace(assemblyName.Name)
                && _probeAssemblyPaths.TryGetValue(assemblyName.Name, out var probePath))
            {
                return LoadFromAssemblyPath(probePath);
            }

            return null;
        }

        private static Dictionary<string, string> BuildProbeAssemblyPaths(string assemblyPath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
            if (string.IsNullOrWhiteSpace(directory))
                return result;

            foreach (var path in EnumerateSafeAssemblyFiles(directory, SearchOption.AllDirectories))
            {
                var simpleName = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrWhiteSpace(simpleName))
                    result.TryAdd(simpleName, path);
            }

            return result;
        }
    }

    private enum ReflectionTypeCategory
    {
        Ignored,
        Infrastructure,
        Candidate
    }

    private sealed class PortableMetadataType
    {
        public string Name { get; init; } = "";
        public string Namespace { get; init; } = "";
        public string FullName { get; init; } = "";
        public string BaseTypeFullName { get; init; } = "";
        public bool IsClass { get; init; }
        public bool IsAbstract { get; init; }
        public bool IsGenericTypeDefinition { get; init; }
        public bool IsNested { get; init; }
        public bool IsEnum { get; init; }
        public bool HasTableAttribute { get; init; }
        public string TableName { get; init; } = "";
        public IReadOnlyList<PortableMetadataProperty> Properties { get; init; } = Array.Empty<PortableMetadataProperty>();

        public bool HasColumnAttributes => Properties.Any(property => property.HasColumnAttribute);
    }

    private sealed class PortableMetadataProperty
    {
        public string Name { get; init; } = "";
        public string TypeName { get; init; } = "";
        public bool IsPublicReadable { get; init; }
        public bool HasColumnAttribute { get; init; }
        public bool HasAssociationAttribute { get; init; }
    }

    private sealed class PortableMetadataTypeAnalysis
    {
        public PortableMetadataType Type { get; init; } = new();
        public ReflectionTypeCategory Category { get; init; }
        public int Score { get; init; }
        public bool ImportFailed { get; set; }
    }

    private sealed class ReflectionTypeAnalysis
    {
        public Type Type { get; init; } = typeof(object);
        public ReflectionTypeCategory Category { get; init; }
        public int Score { get; init; }
        public bool HasTableAttribute { get; init; }
        public bool HasColumnAttributes { get; init; }
        public bool ImportFailed { get; set; }
    }

    private sealed class LoadableTypeSnapshot
    {
        public IReadOnlyList<Type> Types { get; init; } = Array.Empty<Type>();
        public int LoaderExceptionCount { get; init; }
    }
}

