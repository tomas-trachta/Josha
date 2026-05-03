using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Josha.Models
{
    internal class DANamespace(string name, List<DirOD> childNodesTreeOne, List<DirOD> childNodesTreeTwo)
    {
        public string Name { get; set; } = name;
        public List<DirOD> ChildNodesTreeOne { get; set; } = childNodesTreeOne;
        public List<DirOD> ChildNodesTreeTwo { get; set; } = childNodesTreeTwo;
    }
}
