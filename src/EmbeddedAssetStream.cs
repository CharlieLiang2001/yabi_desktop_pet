using System;
using System.IO;
using System.Reflection;

namespace YabiDesktopPet
{
    // The build embeds one resource at CLR manifest offset zero. Reading its
    // on-disk PE section avoids faulting every animation into the process's
    // memory-mapped assembly image. No unpacked files or network are needed.
    internal sealed class EmbeddedAssetStream : Stream
    {
        private readonly FileStream _file;
        private readonly long _start;
        private readonly long _length;
        private long _position;

        public static Stream Open()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] resources = assembly.GetManifestResourceNames();
            if (resources.Length != 1 || resources[0] != AnimationCatalog.ResourceName)
                throw new InvalidDataException("构建必须包含一个亚比动作包资源。");
            FileStream file = new FileStream(assembly.Location, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
            try
            {
                BinaryReader reader = new BinaryReader(file);
                if (reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException("Invalid DOS signature");
                file.Position = 0x3c;
                int pe = reader.ReadInt32();
                if (pe < 0 || pe > file.Length - 24) throw new InvalidDataException("Invalid PE offset");
                file.Position = pe;
                if (reader.ReadUInt32() != 0x4550) throw new InvalidDataException("Invalid PE signature");
                reader.ReadUInt16();
                ushort sections = reader.ReadUInt16();
                file.Position = pe + 20;
                ushort optionalLength = reader.ReadUInt16();
                long optional = pe + 24;
                file.Position = optional;
                ushort magic = reader.ReadUInt16();
                int dataOffset = magic == 0x20b ? 112 : (magic == 0x10b ? 96 : -1);
                if (dataOffset < 0 || optionalLength < dataOffset + 15 * 8) throw new InvalidDataException("Missing CLR directory");
                file.Position = optional + dataOffset + 14 * 8;
                uint cliRva = reader.ReadUInt32();
                long sectionTable = optional + optionalLength;
                long cli = MapRva(reader, sectionTable, sections, cliRva);
                file.Position = cli + 24;
                uint resourceRva = reader.ReadUInt32();
                uint resourceSize = reader.ReadUInt32();
                long resource = MapRva(reader, sectionTable, sections, resourceRva);
                file.Position = resource;
                uint length = reader.ReadUInt32();
                if (resourceSize < 4 || length > resourceSize - 4 || resource + 4 + length > file.Length)
                    throw new InvalidDataException("Embedded resource bounds invalid");
                return new EmbeddedAssetStream(file, resource + 4, length);
            }
            catch { file.Dispose(); throw; }
        }

        private static long MapRva(BinaryReader reader, long table, ushort count, uint rva)
        {
            if (count == 0 || count > 96) throw new InvalidDataException("Invalid PE sections");
            for (int i = 0; i < count; i++)
            {
                reader.BaseStream.Position = table + i * 40 + 8;
                uint virtualSize = reader.ReadUInt32();
                uint address = reader.ReadUInt32();
                uint rawSize = reader.ReadUInt32();
                uint rawOffset = reader.ReadUInt32();
                if ((long)rva >= address && (long)rva < (long)address + Math.Max(virtualSize, rawSize))
                {
                    long offset = (long)rawOffset + rva - address;
                    if (offset < 0 || offset > reader.BaseStream.Length - 4) break;
                    return offset;
                }
            }
            throw new InvalidDataException("CLR resource section not found");
        }

        private EmbeddedAssetStream(FileStream file, long start, long length)
        { _file = file; _start = start; _length = length; }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _length; } }
        public override long Position { get { return _position; } set { Seek(value, SeekOrigin.Begin); } }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int allowed = (int)Math.Min(count, _length - _position);
            _file.Position = _start + _position;
            int read = _file.Read(buffer, offset, allowed);
            _position += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long position = origin == SeekOrigin.Begin ? offset : (origin == SeekOrigin.Current ? _position + offset : _length + offset);
            if (position < 0 || position > _length) throw new IOException("Resource seek is out of range");
            return _position = position;
        }
        public override void Flush() { }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { if (disposing) _file.Dispose(); base.Dispose(disposing); }
    }
}
