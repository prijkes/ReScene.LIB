using Force.Crc32;

namespace ReScene.Core.Cryptography;

public class CRC32
{
    public static string Calculate(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found.", filePath);
        }

        uint hash = 0;
        byte[] buffer = new byte[1048576 * 32]; // 32MB buffer

        using (FileStream entryStream = File.OpenRead(filePath))
        {
            int currentBlockSize = 0;

            while ((currentBlockSize = entryStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash = Crc32Algorithm.Append(hash, buffer, 0, currentBlockSize);
            }
        }

        return hash.ToString("x8");
    }
}
