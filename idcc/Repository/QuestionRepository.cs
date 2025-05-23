﻿using idcc.Context;
using idcc.Models;
using idcc.Models.AdminDto;
using idcc.Models.Dto;
using idcc.Repository.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace idcc.Repository;

public class QuestionRepository : IQuestionRepository
{
    private readonly IdccContext _context;

    public QuestionRepository(IdccContext context)
    {
        _context = context;
    }
    
    public async Task<QuestionDto?> GetQuestionAsync(UserTopic userTopic)
    {
        var weight = await _context.Weights.SingleOrDefaultAsync(_ => _.Grade == userTopic.Grade);
        if (weight is null)
        {
            return null;
        }
        
        var question = await _context.Questions.Where(_ => _.Topic == userTopic.Topic && _.Weight >= userTopic.Weight && _.Weight <= weight.Max).OrderBy(o => Guid.NewGuid()).FirstOrDefaultAsync();
        if (question is null)
        {
            return null;
        }
        
        // TODO проверить на отсутствие ответов к вопросу
        var answers = await _context.Answers.Where(_ => _.Question == question).Select(a => new AnswerDto()
        {
            Id = a.Id,
            Content = a.Content
        }).ToListAsync();

        var dto = new QuestionDto()
        {
            Id = question.Id,
            Topic = userTopic.Topic.Name,
            Content = question.Content,
            Answers = answers
        };

        return dto;
    }

    public async Task<Question?> GetQuestionAsync(int id)
    {
        var question =  await _context.Questions.Where(_ => _.Id == id).FirstOrDefaultAsync();
        return question ?? null;
    }
    
    public async Task<List<Answer>> GetAnswersAsync(Question question)
    {
        var answers = await _context.Answers.Where(_ => _.Question == question).ToListAsync();
        return answers;
    }

    public async Task<List<string>> CreateAsync(List<QuestionAdminDto> questions)
    {
        var notAdded = new List<string>();
        foreach (var question in questions)
        {
            var topic = await _context.Topics.Where(_ => _.Name == question.Topic).SingleOrDefaultAsync();
            if (topic is null)
            {
                notAdded.Add(question.Content);
                break;
            }

            try
            {
                var q = await _context.Questions.AddAsync(new Question()
                {
                    Topic = topic,
                    Content = question.Content,
                    Weight = question.Weight,
                    IsMultipleChoice = question.IsMultipleChoice
                });


                foreach (var answer in question.Answers)
                {
                    await _context.Answers.AddAsync(new Answer()
                    {
                        Question = q.Entity,
                        Content = answer.Content,
                        IsCorrect = answer.IsCorrect
                    });
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                notAdded.Add(question.Content);
            }
        }

        return notAdded;
    }
}