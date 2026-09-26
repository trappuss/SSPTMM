namespace TCFModManager.Core.Markup;

//
// A picture's width in pixels, read from the first bytes of the file - PNG, GIF, JPEG, WebP and BMP,
// the formats sp-mod.com descriptions and thumbnails use.
//
// The app needs the width before decoding, to decode a big picture smaller and never to decode a
// small one larger. Asking the imaging component for it meant building a decoder just to read one
// number, off the UI thread, which is where Wine's imaging crashed the app; this reads the number
// straight out of the header instead.
//
public static class ImageHeader
{
    /// <summary>The width, or null when the bytes are not a format this knows or are cut short.</summary>
    public static int? Width(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30) return null;

        // PNG: the IHDR chunk's width, big-endian, at 16.
        if (bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
            return Positive(BigEndian32(bytes, 16));

        // GIF: the logical screen width, little-endian, at 6.
        if (bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
            return Positive(bytes[6] | (bytes[7] << 8));

        // BMP: a signed width at 18.
        if (bytes[0] == 'B' && bytes[1] == 'M')
            return Positive(Math.Abs(bytes[18] | (bytes[19] << 8) | (bytes[20] << 16) | (bytes[21] << 24)));

        // WebP: RIFF....WEBP, then one of three chunk kinds.
        if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' &&
            bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
        {
            if (bytes[12] == 'V' && bytes[13] == 'P' && bytes[14] == '8')
            {
                switch (bytes[15])
                {
                    // Lossy: 14 bits at 26.
                    case (byte)' ':
                        return Positive((bytes[26] | (bytes[27] << 8)) & 0x3FFF);
                    // Lossless: 14 bits after the 0x2F signature at 20, stored minus one.
                    case (byte)'L':
                        return Positive(((bytes[21] | (bytes[22] << 8)) & 0x3FFF) + 1);
                    // Extended: 24 bits at 24, stored minus one.
                    case (byte)'X':
                        return Positive((bytes[24] | (bytes[25] << 8) | (bytes[26] << 16)) + 1);
                }
            }

            return null;
        }

        // JPEG: walk the segments to the first start-of-frame; the width is at 7 into it.
        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            var at = 2;
            while (at + 9 < bytes.Length)
            {
                if (bytes[at] != 0xFF)
                {
                    at++;
                    continue;
                }

                var marker = bytes[at + 1];

                // Fill bytes and markers that carry no length.
                if (marker == 0xFF || marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
                {
                    at++;
                    continue;
                }

                var length = (bytes[at + 2] << 8) | bytes[at + 3];

                // SOF0-SOF15, except DHT (C4), JPG (C8) and DAC (CC).
                if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    return Positive((bytes[at + 7] << 8) | bytes[at + 8]);

                if (length < 2) return null;
                at += 2 + length;
            }
        }

        return null;
    }

    private static int BigEndian32(ReadOnlySpan<byte> bytes, int at) =>
        (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];

    private static int? Positive(int value) => value > 0 ? value : null;

    //
    // Whether a GIF has more than one frame - an animation to play rather than a still to decode.
    //
    // Walks the file's blocks as the format lays them out (GIF89a: header, screen descriptor, the
    // colour table, then extensions and images until the trailer), stopping at the second image. A
    // file cut short or not a GIF is not an animation. Only a first look is needed, so a file of
    // many frames costs no more than reading up to its second.
    //
    public static bool IsAnimatedGif(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 13 || bytes[0] != 'G' || bytes[1] != 'I' || bytes[2] != 'F') return false;

        var at = 13;
        if ((bytes[10] & 0x80) != 0) at += 3 << ((bytes[10] & 0x07) + 1);

        var images = 0;
        while (at < bytes.Length)
        {
            switch (bytes[at])
            {
                case 0x21: // an extension: its label, then its sub-blocks
                    at += 2;
                    if (!SkipSubBlocks(bytes, ref at)) return false;
                    break;

                case 0x2C: // an image: its descriptor, a local colour table, the LZW size, the data
                    if (++images > 1) return true;
                    if (at + 10 > bytes.Length) return false;
                    var flags = bytes[at + 9];
                    at += 10;
                    if ((flags & 0x80) != 0) at += 3 << ((flags & 0x07) + 1);
                    at += 1;
                    if (!SkipSubBlocks(bytes, ref at)) return false;
                    break;

                default: // 0x3B, the trailer - or something that is not a GIF block
                    return false;
            }
        }

        return false;
    }

    // Sub-blocks are a length byte and that many bytes, ended by a zero length.
    private static bool SkipSubBlocks(ReadOnlySpan<byte> bytes, ref int at)
    {
        while (at < bytes.Length)
        {
            var length = bytes[at];
            at += 1 + length;
            if (length == 0) return true;
        }

        return false;
    }
}
