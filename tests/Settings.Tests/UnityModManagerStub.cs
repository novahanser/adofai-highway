// This stub only allows the production settings type to compile without game DLLs.
// Persistence tests use the real framework XmlSerializer, never this Save method.
namespace UnityModManagerNet
{
    public class UnityModManager
    {
        public class ModEntry { }

        public class ModSettings
        {
            public virtual void Save(ModEntry modEntry) =>
                throw new NotSupportedException("UMM persistence is outside these tests.");

            protected static void Save<T>(T settings, ModEntry modEntry) where T : ModSettings =>
                throw new NotSupportedException("Use XmlSerializer for the persistence tests.");
        }
    }
}
