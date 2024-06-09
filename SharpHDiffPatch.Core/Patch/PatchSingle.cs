using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using SharpHDiffPatch.Core.Binary.Compression;


namespace SharpHDiffPatch.Core.Patch
{
    public sealed class PatchSingle : HDiffPatchFile
    {
        public PatchSingle(HDiffPatch hDiffPatch, HeaderInfo headerInfo, string input, string output, bool useBufferedPatch, bool useFullBuffer, bool useFastBuffer, CancellationToken token) : base(hDiffPatch, headerInfo, input, output, token, useBufferedPatch, useFullBuffer, useFastBuffer)
        {
        }

        public override void Patch()
        {
            using (FileStream inputStream = new FileStream(this.basePathInput, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream outputStream = new FileStream(this.basePathOutput, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                if (inputStream.Length != headerInfo.oldDataSize)
                    throw new InvalidDataException($"[PatchSingle::Patch] The patch directory is expecting old size to be equivalent as: {headerInfo.oldDataSize} bytes, but the input file has unmatch size: {inputStream.Length} bytes!");

                HDiffPatch.Event.PushLog($"[PatchSingle::Patch] Existing old file size: {inputStream.Length} is matched!", Verbosity.Verbose);
                HDiffPatch.Event.PushLog($"[PatchSingle::Patch] Staring patching routine at position: {headerInfo.chunkInfo.headEndPos}", Verbosity.Verbose);

                IPatchCore patchCore = null;
                if (useFastBuffer && useBufferedPatch)
                    patchCore = new PatchCoreFastBuffer(this.HDiffPatch, token, headerInfo.newDataSize, Stopwatch.StartNew(), this.basePathInput, this.basePathOutput);
                else
                    patchCore = new PatchCore(this.HDiffPatch, token, headerInfo.newDataSize, Stopwatch.StartNew(), this.basePathInput, this.basePathOutput);

                StartPatchRoutine(inputStream, outputStream, patchCore);
            }
        }

        private void StartPatchRoutine(Stream inputStream, Stream outputStream, IPatchCore patchCore)
        {
            bool isCompressed = headerInfo.compMode != CompressionMode.nocomp;

            Stream[] clips = new Stream[4];
            Stream[] sourceClips = new Stream[4]
            {
                spawnPatchStream(),
                spawnPatchStream(),
                spawnPatchStream(),
                spawnPatchStream()
            };

            int padding = headerInfo.compMode == CompressionMode.zlib ? 1 : 0;

            try
            {
                long offset = headerInfo.chunkInfo.headEndPos;
                int coverPadding = headerInfo.chunkInfo.compress_cover_buf_size > 0 ? padding : 0;
                clips[0] = patchCore.GetBufferStreamFromOffset(headerInfo.compMode, sourceClips[0], offset + coverPadding,
                    headerInfo.chunkInfo.cover_buf_size, headerInfo.chunkInfo.compress_cover_buf_size, out long nextLength, useBufferedPatch, false);

                offset += nextLength;
                int rle_ctrlBufPadding = headerInfo.chunkInfo.compress_rle_ctrlBuf_size > 0 ? padding : 0;
                clips[1] = patchCore.GetBufferStreamFromOffset(headerInfo.compMode, sourceClips[1], offset + rle_ctrlBufPadding,
                    headerInfo.chunkInfo.rle_ctrlBuf_size, headerInfo.chunkInfo.compress_rle_ctrlBuf_size, out nextLength, useBufferedPatch, useFastBuffer);

                offset += nextLength;
                int rle_codeBufPadding = headerInfo.chunkInfo.compress_rle_codeBuf_size > 0 ? padding : 0;
                clips[2] = patchCore.GetBufferStreamFromOffset(headerInfo.compMode, sourceClips[2], offset + rle_codeBufPadding,
                    headerInfo.chunkInfo.rle_codeBuf_size, headerInfo.chunkInfo.compress_rle_codeBuf_size, out nextLength, useBufferedPatch, useFastBuffer);

                offset += nextLength;
                int newDataDiffPadding = headerInfo.chunkInfo.compress_newDataDiff_size > 0 ? padding : 0;
                clips[3] = patchCore.GetBufferStreamFromOffset(headerInfo.compMode, sourceClips[3], offset + newDataDiffPadding,
                    headerInfo.chunkInfo.newDataDiff_size, headerInfo.chunkInfo.compress_newDataDiff_size - padding, out _, useBufferedPatch && useFullBuffer, false);

                patchCore.UncoverBufferClipsStream(clips, inputStream, outputStream, headerInfo);
            }
            catch
            {
                throw;
            }
            finally
            {
                foreach (Stream clip in clips) clip?.Dispose();
                foreach (Stream clip in sourceClips) clip?.Dispose();
            }
        }
    }
}
