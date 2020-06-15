﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using DungeonTools.Save.File;

namespace DungeonTools.Cli {
    public static class Program {
        private static async Task Main(string[] args) {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;

            await Parser.Default.ParseArguments<Options>(args).WithParsedAsync(MainWithParsed);
        }

        private static async Task MainWithParsed(Options options) {
            foreach(string filePath in options.Input) {
                await ProcessFile(new FileInfo(filePath), options.Overwrite);
            }
        }

        private static async ValueTask ProcessFile(FileInfo file, bool overwrite) {
            if(!file.Exists) {
                await Console.Error.WriteLineAsync($"[  ERROR  ] File \"{file.FullName}\" could not be found and has been skipped.");
                return;
            }

            await using FileStream inputStream = file.OpenRead();
            bool encrypted = SaveFileHandler.IsFileEncrypted(inputStream);

            Stream? processed;
            if (encrypted)
            {
                processed = await Decrypt(inputStream);
            } else
            {
                processed = await Encrypt(inputStream);
            }
            if(processed == null) {
                await Console.Out.WriteLineAsync($"[  ERROR  ] Content of file \"{file.Name}\" could not be converted to a supported format.");
                return;
            }

            processed.Position = 0;
            string outputFile = GetOutputFilePath(file, encrypted, overwrite);
            await using FileStream outputStream = File.Open(outputFile, FileMode.Create, FileAccess.Write);
            await processed.CopyToAsync(outputStream);
        }
        
        private static async ValueTask<Stream?> Decrypt(Stream data) {
            Stream decrypted = await EncryptionProviders.Current.DecryptAsync(data);
            return SaveFileHandler.RemoveTrailingZeroes(decrypted);
        }
        
        private static async ValueTask<Stream?> Encrypt(Stream data) {
            await using var memStreamHax = new MemoryStream((int)(data.Length-data.Position));
            await data.CopyToAsync(memStreamHax);
            while (memStreamHax.Position % 16 != 0)
            {
                memStreamHax.Write(new byte[] {0});
            }
            memStreamHax.Position = 0;
            
            await using Stream encrypted = await EncryptionProviders.Current.EncryptAsync(memStreamHax);
            return SaveFileHandler.PrependMagicToEncrypted(encrypted);
        }

        private static string GetOutputFilePath(FileInfo fileInfo, bool isEncrypted, bool overwrite) {
            string targetExtension = fileInfo.Extension.ToUpperInvariant() switch {
                "" => isEncrypted ? ".json" : "", // Special case for Switch which has no file extension
                _ => isEncrypted ? ".json" : ".dat",
            };

            string idealFileName = $"{Path.GetFileNameWithoutExtension(fileInfo.Name)}{targetExtension}";
            if(string.Equals(fileInfo.Name, idealFileName, StringComparison.CurrentCultureIgnoreCase)) {
                idealFileName = $"{Path.GetFileNameWithoutExtension(idealFileName)}_{(isEncrypted ? "Decrypted" : "Encrypted")}{targetExtension}";
            }

            string outFileName = Path.Combine(fileInfo.DirectoryName, idealFileName);
            if(overwrite || !File.Exists(outFileName)) {
                return outFileName;
            }

            int fileNumber = 1;
            while(File.Exists(outFileName)) {
                outFileName = $"{outFileName.Substring(outFileName.Length - targetExtension.Length)}_{fileNumber++}{targetExtension}";
            }

            return outFileName;
        }
    }
}
