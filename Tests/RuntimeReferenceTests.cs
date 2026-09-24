using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace RimMind.Core.Tests
{
    public class RuntimeReferenceTests
    {
        [Fact]
        public void BuiltMod_UsesOnlyPackagedRuntimeDependencies()
        {
            string dll = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "1.6", "Assemblies", "RimMindCore.dll"));

            using var stream = File.OpenRead(dll);
            using var pe = new PEReader(stream);
            MetadataReader metadata = pe.GetMetadataReader();
            string[] references = metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
                .ToArray();

            Assert.DoesNotContain("System.Net.Http", references);
            Assert.Contains("Newtonsoft.Json", references);

            string jsonDll = Path.Combine(Path.GetDirectoryName(dll)!, "Newtonsoft.Json.dll");
            Assert.True(File.Exists(jsonDll), "Newtonsoft.Json.dll must be deployed beside RimMindCore.dll.");
            Assert.Equal(new Version(13, 0, 0, 0), AssemblyName.GetAssemblyName(jsonDll).Version);
        }
    }
}
