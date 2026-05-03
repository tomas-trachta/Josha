using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Josha.Models
{
    internal class DirOD(string name, string path)
    {
        public string Name { get; set; } = name;
        public string Path { get; set; } = path;
        public decimal SizeKiloBytes { get; set; } = 0;

        // 0 = unknown (e.g. snapshot from older format)
        public long LastWriteTimeUtcTicks { get; set; }

        public bool IsScanned { get; set; }

        public DirOD[] Subdirectories { get; set; } = [];
        public FileOD[] Files { get; set; } = [];

        internal DirOD? FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var target = path.TrimEnd('\\', '/');
            var ours = Path.TrimEnd('\\', '/');
            if (string.Equals(ours, target, StringComparison.OrdinalIgnoreCase))
                return this;

            var ourPrefix = ours + System.IO.Path.DirectorySeparatorChar;
            if (!target.StartsWith(ourPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            foreach (var sub in Subdirectories)
            {
                var found = sub.FindByPath(path);
                if (found != null) return found;
            }
            return null;
        }

        // Bumped on every mutation of Subdirectories/Files. Concurrent reconcilers
        // snapshot it before off-dispatcher I/O and abort if it moves — protects
        // against overwriting an entry a file op inserted in the meantime.
        private long _mutationVersion;
        public long MutationVersion => Interlocked.Read(ref _mutationVersion);
        public long BumpMutationVersion() => Interlocked.Increment(ref _mutationVersion);

        internal decimal GetDirSize()
        {
            return GetDirSize(this, 0);
        }

        internal decimal GetDirSizeSequential()
        {
            return GetDirSizeSequential(this);
        }

        private static decimal GetDirSizeSequential(DirOD dir)
        {
            if (dir == null) return 0;

            var size = dir.Files.Sum(x => x.SizeKiloBytes);

            foreach (var subDir in dir.Subdirectories)
            {
                size += GetDirSizeSequential(subDir);
            }

            dir.SizeKiloBytes = size;
            return size;
        }

        private decimal GetDirSize(DirOD dir, int level)
        {
            if(dir == null) return 0;

            var size = dir.Files.Sum(x => x.SizeKiloBytes);

            if (dir.Subdirectories.Length == 0)
            {
                dir.SizeKiloBytes = size;
                return size;
            }

            if(level < 2)
            {
                Lock sizeLock = new();

                Parallel.ForEach(dir.Subdirectories, (subdir) =>
                {
                    var subdirSize = GetDirSize(subdir, level + 1);
                    lock (sizeLock)
                    {
                        size += subdirSize;
                    }
                });
            }
            else
            {
                foreach (var subDir in dir.Subdirectories)
                {
                    size += GetDirSize(subDir, level + 1);
                }
            }

            dir.SizeKiloBytes = size;
            return size;
        }
    }
}
