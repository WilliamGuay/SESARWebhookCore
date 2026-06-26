using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SESARLightUtils
{
  public static class Constants
  {
    public const int HEADER_PARAMETERS_SIZE_IN_BYTE_BYTE_RANGE = 8;
    public const int IV_SIZE_IN_BYTES = 12;
    public const int TAG_SIZE_IN_BYTES = 16;
    public const int KEY_SIZE_IN_BYTES = 32;
    public const int CHECKSUM_SIZE_IN_BYTES = 12;

    public const int UPLOAD_CHUNK_SIZE = 10485760;
  }
}
