﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WDE.Common.Services.MessageBox;
using WDE.Module.Attributes;
using WDE.PacketViewer.Settings;
using WowPacketParser.PacketStructures;
using WowPacketParser.Proto;

//using WowPacketParser.PacketStructures;

namespace WDE.PacketViewer.PacketParserIntegration
{
    [AutoRegister]
    [SingleInstance]
    public class SniffLoader : ISniffLoader
    {
        private readonly IPacketParserService packetParserService;
        private readonly IMessageBoxService messageBoxService;
        private readonly IPacketViewerSettings viewerSettings;

        public SniffLoader(IPacketParserService packetParserService,
            IMessageBoxService messageBoxService, IPacketViewerSettings viewerSettings)
        {
            this.packetParserService = packetParserService;
            this.messageBoxService = messageBoxService;
            this.viewerSettings = viewerSettings;
        }
        
        public async Task<Packets> LoadSniff(string path, int? customVersion, CancellationToken token, IProgress<float> progress)
        {
            Packets packets = null!;
            var extension = Path.GetExtension(path);

            if (extension != ".pkt" && extension != ".bin" && extension != ".dat")
            {
                extension = await messageBoxService.ShowDialog(new MessageBoxFactory<string>()
                    .SetTitle("Unknown file type")
                    .SetMainInstruction("File doesn't have .pkt, .bin nor .dat extension")
                    .SetContent("Therefore, cannot determine whether to open the file as parsed sniff or unparsed sniff")
                    .WithButton("Open as parsed sniff", ".dat")
                    .WithButton("Open as raw sniff", ".pkt")
                    .Build());
            }
            
            if (extension == ".pkt" || extension == ".bin")
            {
                progress.Report(-1);
                var parsedPath = Path.ChangeExtension(path, null) + "_parsed.dat";
                var parsedTextPath = Path.ChangeExtension(path, null) + "_parsed.txt";
                if (IsFileInUse(parsedTextPath) || IsFileInUse(parsedPath))
                    throw new ParserException("Sniff output file already in use, probably in the middle of parsing, thus can't parse sniff. Are you parsing it in another window already?");    

                var versionType = await GetVersionAndDump(parsedPath);
                bool runParser = !File.Exists(parsedPath) || !versionType.HasValue ||
                                 versionType.Value.Item1 != StructureVersion.ProtobufStructureVersion ||
                                 (versionType.Value.Item2 == DumpFormatType.UniversalProtoWithSeparateText && !File.Exists(parsedTextPath));

                if (runParser)
                {
                    await packetParserService.RunParser(path, viewerSettings.Settings.Parser, DumpFormatType.UniversalProtoWithSeparateText, customVersion, token, progress);
                    if (token.IsCancellationRequested)
                        File.Delete(parsedPath);
                }
                path = parsedPath;
            }
            else if (extension == ".dat")
            {
                var parsedTextPath = Path.ChangeExtension(path, "txt");
                var versionType = await GetVersionAndDump(path); 
                bool runParser = !versionType.HasValue ||
                                 versionType.Value.Item1 != StructureVersion.ProtobufStructureVersion ||
                                 (versionType.Value.Item2 == DumpFormatType.UniversalProtoWithSeparateText && !File.Exists(parsedTextPath));
                
                if (runParser)
                {
                    throw new ParserException(
                        "You have opened _parsed.dat file generated by an older version of the editor.\nIt is not compatible. Please reparse the sniff by reopening the .pkt file");
                }
            }

            if (token.IsCancellationRequested)
                return packets;

            if (!File.Exists(path))
            {
                throw new ParserException(
                    "For some reason parser didn't generate the output file. Aborting. If it repeats, report bug on github.");
            }
            
            progress.Report(-1);
            await Task.Run(async () =>
            {
                await using var input = File.OpenRead(path);
                try
                {
                    packets = Packets.Parser.ParseFrom(input);
                }
                catch (Google.Protobuf.InvalidProtocolBufferException)
                {
                    throw new ParserException("Sniff file is broken. Possible reason: you are trying to open _parsed.dat file generated with an older editor version. Select .pkt file and reparse again.");
                }
            }, token).ConfigureAwait(true);
            progress.Report(1);

            return packets;
        }

        private bool IsFileInUse(string path)
        {
            if (!File.Exists(path))
                return false;
            
            try
            {
                File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None).Dispose();
            }
            catch (Exception)
            {
                return true;
            }

            return false;
        }

        public async Task<(ulong, DumpFormatType)?> GetVersionAndDump(string path)
        {
            await using var input = File.OpenRead(path);
            try
            {
                var version = PacketsVersion.Parser.ParseFrom(input);
                input.Close();
                return (version.Version, (DumpFormatType)version.DumpType);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}