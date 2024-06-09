using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace SharpHDiffPatch.Core.Patch
{
    public abstract class HDiffPatchFile : IDisposable
    {
        private int isDisposed;
        protected readonly HDiffPatch HDiffPatch;
        protected HeaderInfo headerInfo;
        // protected readonly Func<FileStream> spawnPatchStream;

        protected readonly string basePathInput;
        protected readonly string basePathOutput;
        protected readonly bool useBufferedPatch;
        protected readonly bool useFullBuffer;
        protected readonly bool useFastBuffer;

        protected readonly CancellationToken token;
        private readonly MemoryMappedFile hDiffPatchFileOnDisk;

#if USEEXPERIMENTALMULTITHREAD
        protected readonly bool useMultiThread;
#endif

        protected HDiffPatchFile(HDiffPatch hDiffPatch, HeaderInfo headerInfo, string input, string output, CancellationToken token
#if USEEXPERIMENTALMULTITHREAD
            , bool useMultiThread
#endif
            , bool useBufferedPatch = true, bool useFullBuffer = false, bool useFastBuffer = false)
        {
            this.HDiffPatch = hDiffPatch;
            this.token = token;
            this.headerInfo = headerInfo;
            this.basePathInput = input;
            this.basePathOutput = output;
            this.useBufferedPatch = useBufferedPatch;
            this.useFullBuffer = useFullBuffer;
            this.useFastBuffer = useFastBuffer;

#if USEEXPERIMENTALMULTITHREAD
            this.useMultiThread = useMultiThread;
#endif
            this.hDiffPatchFileOnDisk = MemoryMappedFile.CreateFromFile(headerInfo.patchPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            // spawnPatchStream = new Func<FileStream>(() => new FileStream(headerInfo.patchPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        protected Stream spawnPatchStream() => this.hDiffPatchFileOnDisk.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);

        public abstract void Patch();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.isDisposed, 1) == 0)
            {
                GC.SuppressFinalize(this);
                this.Dispose(true);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            this.hDiffPatchFileOnDisk.Dispose();
        }

        ~HDiffPatchFile()
        {
            this.Dispose(false);
        }
    }
}
