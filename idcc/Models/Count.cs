﻿using System.ComponentModel.DataAnnotations;

namespace idcc.Models;

public class Count
{
    [Key]
    public int Id { get; set; }
    
    public string Code { get; set; }
    public string Description { get; set; }
    public int Value { get; set; }
}