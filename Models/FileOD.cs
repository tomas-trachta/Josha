using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Josha.Models
{
    internal class FileOD(string name, decimal size)
    {
        public string Name { get; set; } = name;
        public decimal SizeKiloBytes { get; set; } = size;
    }
}
