using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Josha.CustomExceptions
{
    public class RootDirectoryAnalysisException(string message) : Exception(message);
    public class GetDirectoryNameException(string message) : Exception(message);
}
