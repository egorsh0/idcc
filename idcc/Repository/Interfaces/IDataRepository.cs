﻿using idcc.Infrastructures;
using idcc.Models;
using idcc.Models.AdminDto;

namespace idcc.Repository.Interfaces;

public interface IDataRepository
{
    Task<(double average, double min, double max)?> GetGradeTimeInfoAsync(int gradeId);
    
    Task<(double min, double max)?> GetGradeWeightInfoAsync(int gradeId);
    Task<(double, Grade)> GetGradeLevelAsync(double score);

    Task<double> GetPercentOrDefaultAsync(string code, double value);
    Task<int> GetCountOrDefaultAsync(string code, int value);

    Task<(Grade? prev, Grade? next)> GetRelationAsync(Grade current);
}