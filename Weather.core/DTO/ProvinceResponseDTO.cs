﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weather.core.DTO
{
    public class ProvinceResponseDTO
    {
        // Provinces list
        public List<ProvinceDTO> provincias { get; set; } = new();
    }
}
