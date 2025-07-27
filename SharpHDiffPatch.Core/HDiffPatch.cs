using SharpHDiffPatch.Core.Event;
using SharpHDiffPatch.Core.Patch;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SharpHDiffPatch.Core.Binary.Compression;

namespace SharpHDiffPatch.Core
{
    public enum BufferMode { None, Partial, Full }
    public enum Verbosity { Quiet, Info, Verbose, Debug }

    public enum ChecksumMode
    {
        nochecksum,
        crc32,
        fadler64
    }

    public struct HeaderInfoExt
    {
        public HeaderInfo headerInfo;
        public DataReferenceInfo dataReferenceInfo;
    }

    public struct HeaderInfo
    {
        public CompressionMode compMode;
        public ChecksumMode checksumMode;

        public bool isInputDir;
        public bool isOutputDir;
        public bool isSingleCompressedDiff;
        public string patchPath;
        public Func<Stream> patchCreateStream;
        public string headerMagic;  

        public long stepMemSize;

        public bool dirDataIsCompressed;

        public long oldDataSize;
        public long newDataSize;
        public long compressedCount;

        public DiffSingleChunkInfo singleChunkInfo;
        public DiffChunkInfo chunkInfo;
    }

    public struct DataReferenceInfo
    {
        public long inputDirCount;
        public long inputRefFileCount;
        public long inputRefFileSize;
        public long inputSumSize;

        public long outputDirCount;
        public long outputRefFileCount;
        public long outputRefFileSize;
        public long outputSumSize;

        public long sameFilePairCount;
        public long sameFileSize;

        public int newExecuteCount;

        public long privateReservedDataSize;
        public long privateExternDataSize;
        public long privateExternDataOffset;

        public long externDataOffset;
        public long externDataSize;

        public long compressSizeBeginPos;

        public byte checksumByteSize;
        public long checksumOffset;

        public long headDataSize;
        public long headDataOffset;
        public long headDataCompressedSize;

        public long hdiffDataOffset;
        public long hdiffDataSize;
    }

    public struct DiffSingleChunkInfo
    {
        public long uncompressedSize;
        public long compressedSize;

        public long diffDataPos;
    }

    public struct DiffChunkInfo
    {
        public long typesEndPos;
        public long coverCount;
        public long compressSizeBeginPos;
        public long cover_buf_size;
        public long compress_cover_buf_size;
        public long rle_ctrlBuf_size;
        public long compress_rle_ctrlBuf_size;
        public long rle_codeBuf_size;
        public long compress_rle_codeBuf_size;
        public long newDataDiff_size;
        public long compress_newDataDiff_size;
        public long headEndPos;
        public long coverEndPos;
    }

    public sealed class HDiffPatch
    {
        public readonly HeaderInfo headerInfo;
        public readonly DataReferenceInfo referenceInfo;
        public readonly Stream diffStream;
        public readonly bool isPatchDir;

        public readonly PatchEvent PatchEvent = new PatchEvent();
        public readonly static EventListener Event = new EventListener();

        public static Verbosity LogVerbosity { get; set; } = Verbosity.Quiet;

        #region Header Initialization
        public HDiffPatch(string diff)
        {
            using (diffStream = new FileStream(diff, FileMode.Open, FileAccess.Read))
            {
                isPatchDir = Header.TryParseHeaderInfo(diffStream, diff, out HeaderInfo info, out DataReferenceInfo reference);

                headerInfo = info;
                referenceInfo = reference;
            }
        }

        public HDiffPatch(Func<Stream> diffCreateStream)
        {
            using (diffStream = diffCreateStream())
            {
                isPatchDir = Header.TryParseHeaderInfo(diffStream, null, out HeaderInfo info, out DataReferenceInfo reference);

                headerInfo = info;
                referenceInfo = reference;
                headerInfo.patchCreateStream = diffCreateStream;
            }
        }

        public void Patch(string inputPath, string outputPath, bool useBufferedPatch, CancellationToken token = default, bool useFullBuffer = false, bool useFastBuffer = false
#if USEEXPERIMENTALMULTITHREAD
            , bool useMultiThread = false
#endif
        )
            => Patch(inputPath, outputPath, useBufferedPatch, null, token, useFullBuffer, useFastBuffer);

        public void Patch(string inputPath, string outputPath, bool useBufferedPatch, Action<long> writeBytesDelegate, CancellationToken token = default, bool useFullBuffer = false, bool useFastBuffer = false
#if USEEXPERIMENTALMULTITHREAD
            , bool useMultiThread = false
#endif
            )
        {
            IPatch patcher;
            if (isPatchDir && headerInfo.isInputDir && headerInfo.isOutputDir)
            {
                patcher = new PatchDir(this, headerInfo, referenceInfo, headerInfo.patchPath, token
#if USEEXPERIMENTALMULTITHREAD
                    useMultiThread
#endif
                );
            }
            else
            {
                patcher = new PatchSingle(this, headerInfo, token);
            }
            patcher.Patch(inputPath, outputPath, writeBytesDelegate, useBufferedPatch, useFullBuffer, useFastBuffer);
        }
#endregion

        internal void UpdateEvent(long read, ref long currentSizePatched, ref long totalSizePatched, Stopwatch patchStopwatch)
        {
            lock (PatchEvent)
            {
                PatchEvent.UpdateEvent(currentSizePatched += read, totalSizePatched, read, patchStopwatch.Elapsed.TotalSeconds);
                Event.PushEvent(PatchEvent);
            }
        }

        public static long GetHDiffNewSize(string diffFilePath)
        {
            HeaderInfoExt headerInfo = GetHDiffHeaderInfo(diffFilePath);
            return headerInfo.headerInfo.newDataSize;
        }

        public static long GetHDiffOldSize(string diffFilePath)
        {
            HeaderInfoExt headerInfo = GetHDiffHeaderInfo(diffFilePath);
            return headerInfo.headerInfo.oldDataSize;
        }

        public static long GetHDiffNewSize(Stream diffStream)
        {
            HeaderInfoExt headerInfo = GetHDiffHeaderInfo(diffStream);
            return headerInfo.headerInfo.newDataSize;
        }

        public static long GetHDiffOldSize(Stream diffStream)
        {
            HeaderInfoExt headerInfo = GetHDiffHeaderInfo(diffStream);
            return headerInfo.headerInfo.oldDataSize;
        }

        public static HeaderInfoExt GetHDiffHeaderInfo(string diffFilePath)
        {
            using FileStream fs = new FileStream(diffFilePath, FileMode.Open, FileAccess.Read);
            _ = Header.TryParseHeaderInfo(fs, diffFilePath, out HeaderInfo headerInfo, out DataReferenceInfo headerInfoReference);
            return new HeaderInfoExt { headerInfo = headerInfo, dataReferenceInfo = headerInfoReference };
        }

        public static HeaderInfoExt GetHDiffHeaderInfo(Stream diffStream)
        {
            _ = Header.TryParseHeaderInfo(diffStream, null, out HeaderInfo headerInfo, out DataReferenceInfo headerInfoReference);
            return new HeaderInfoExt { headerInfo = headerInfo, dataReferenceInfo = headerInfoReference };
        }
    }

    public class EventListener
    {
        // Log for external listener
        public static event EventHandler<PatchEvent> PatchEvent;
        public static event EventHandler<LoggerEvent> LoggerEvent;
        public void PushEvent(PatchEvent patchEvent) { }
        public void PushLog(in string message, Verbosity logLevel = Verbosity.Info) { }
    }
}
