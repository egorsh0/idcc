﻿using System.Text.RegularExpressions;
using idcc.Bot.Helpers;
using idcc.Bot.Models;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace idcc.Bot.Services;

public class UpdateHandler(ITelegramBotClient bot, IIdccService idccService, ILogger<UpdateHandler> logger) : IUpdateHandler
{
    private DateTime _questionTime = default!;

    private readonly string _pattern = @"question\((.+),answer:(.+)\)";

    public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        logger.LogInformation("HandleError: {Exception}", exception);
        // Cooldown in case of network connection error
        if (exception is RequestException)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await (update switch
        {
            { Message: { } message }                        => OnMessage(message),
            { CallbackQuery: { } callbackQuery }            => OnCallbackQuery(callbackQuery),
            // ChannelPost:
            // EditedChannelPost:
            // ShippingQuery:
            // PreCheckoutQuery:
            _                                               => UnknownUpdateHandlerAsync(update)
        });
    }
    
    private async Task OnMessage(Message msg)
    {
        logger.LogInformation("Receive message type: {MessageType}", msg.Type);
        if (msg.Text is not { } messageText)
            return;

        Message sentMessage = await (messageText.Split(' ')[0] switch
        {
            "/start" => Start(msg),
            "/testing" => StartSession(msg),
            "/question" => GetQuestion(msg),
            "/report" => GetReport(msg, msg.From.Id.ToString()),
            _ => throw new ArgumentOutOfRangeException()
        });
        logger.LogInformation("The message was sent with id: {SentMessageId}", sentMessage.MessageId);
    }
    
    async Task<Message> Start(Message msg)
    {
        logger.LogInformation("Create user");

        var callbackAnswerData = new List<InlineKeyboardButton[]>()
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Начать тестирование", $"start_testing")
            }
        };
            
        var inlineKeyboard = new InlineKeyboardMarkup(callbackAnswerData);
                  
        _questionTime = DateTime.Now;
        return await bot.SendTextMessageAsync(
            msg.Chat.Id,
            "Добро пожаловать на тестирование своих навыков.",
            replyMarkup: inlineKeyboard);
    }
    
    async Task<Message> StartSession(Message msg)
    {
        var inlineKeyboard = new InlineKeyboardMarkup(
            new List<InlineKeyboardButton[]>
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("QA", "qa")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("SA", "sa")
                }
            });
                                
        return await bot.SendTextMessageAsync(
            msg.Chat.Id,
            "Выберите вашу роль",
            replyMarkup: inlineKeyboard); // Все клавиатуры передаются в параметр replyMarkup
    }
    
    async Task<Message> GetQuestion(Message msg)
    {
        bool next;
        QuestionDto question;
        do
        {
            (question, var message, next) = await idccService.GetQuestionAsync(msg.Chat?.Id.ToString());

            if (question is not null)
            {
                break;
            }

            if (question is null && next == false)
            {
                break;
            }
            if (message is not null)
            {
                var mes = await bot.SendTextMessageAsync(msg.Chat, message.Message, parseMode: ParseMode.Html,
                    replyMarkup: new ReplyKeyboardRemove());
                Thread.Sleep(5000);
                await bot.DeleteMessageAsync(chatId: msg.Chat, messageId: mes.MessageId);
                next = true;
            }
        } while (next);

        if (question is null && next == false)
        {
            await idccService.StopSessionAsync(msg.Chat.Id.ToString());
            await bot.SendTextMessageAsync(msg.Chat, "Тестирование завершено", parseMode: ParseMode.Html,
                replyMarkup: new ReplyKeyboardRemove());
            
            var listButtor = new List<InlineKeyboardButton[]>()
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Получить отчет", $"generate_report")
                }
            };
            
            var keyboard = new InlineKeyboardMarkup(listButtor);
                  
            _questionTime = DateTime.Now;
            return await bot.SendTextMessageAsync(
                msg.Chat.Id,
                "Тестирование завершено.",
                replyMarkup: keyboard);
        }
        
        var answers = ListHelpers.ShuffleArray(question?.Answers.ToArray());
        var callbackAnswerData = new List<InlineKeyboardButton[]>();

        foreach (var answer in answers)
        {
            var answerData = new[]
            {
                InlineKeyboardButton.WithCallbackData(answer.Content, $"question({question?.Id},answer:{answer.Id})")
            };
            callbackAnswerData.Add(answerData);
        }
        var inlineKeyboard = new InlineKeyboardMarkup(callbackAnswerData);
                  
        _questionTime = DateTime.Now;
        return await bot.SendTextMessageAsync(
            msg.Chat.Id,
            question?.Content!,
            replyMarkup: inlineKeyboard);
    }
    
    async Task<Message> GetReport(Message msg, string id)
    {
        var (report, message) = await idccService.GetReportAsync(id);

        if (message is not null)
        {
            return await bot.SendTextMessageAsync(msg.Chat, message.Message, parseMode: ParseMode.Html, replyMarkup: new ReplyKeyboardRemove());
        }

        if (report?.TopicReport is not null)
        {
            var ms = new MemoryStream(report.TopicReport);
            await bot.SendPhotoAsync(msg.Chat.Id, new InputFileStream(ms));
        }
        
        return await bot.SendTextMessageAsync(
            msg.Chat.Id,
            $@"Отчет сформирован. Ваш балл: {report.FinalScoreDto.Score}. Ваш уровень: {report.FinalScoreDto.Grade}",
            parseMode: ParseMode.Html, replyMarkup: new ReplyKeyboardRemove());
    }
    
    // Process Inline Keyboard callback data
    private async Task<Message> OnCallbackQuery(CallbackQuery callbackQuery)
    {
        logger.LogInformation("Received inline keyboard callback from: {CallbackQueryId}", callbackQuery.Id);
        var user = callbackQuery.From;
        logger.LogInformation($"{user.FirstName} ({user.Id}) нажал на кнопку: {callbackQuery.Data}");
        var chat = callbackQuery.Message?.Chat;

        var data = callbackQuery.Data?.ToLower();
        var parseData = ParseCallbackData(callbackQuery.Data?.ToLower()!);

        switch (data)
        {
            case "qa": case "sa":
            {
                var userError = await idccService.GetUserAsync(user.Id.ToString());

                if (userError is not null)
                {
                    var username = user.Username ?? user.FirstName;
                    var (_, message) = await idccService.CreateUserAsync(user.Id.ToString(),username);
                    if (message is not null)
                    {
                        logger.LogInformation(message.Message);
                        return await bot.SendTextMessageAsync(chat!,
                            $"Ошибка при создании пользователя {message.Message}");
                    }

                    logger.LogInformation("Пользователь создан!");
                }

                await idccService.StopSessionAsync(user.Id.ToString());
                
                var role = data == "qa" ? "QA" : "SA";
                
                var (_, error) = await idccService.StartSessionAsync(user.Id.ToString(), role);
                if (error is not null)
                {
                    return await bot.SendTextMessageAsync(chat!, error.Message, parseMode: ParseMode.Html, replyMarkup: new ReplyKeyboardRemove());
                }
                
                await bot.SendTextMessageAsync(chat!, "Сессия запущена", parseMode: ParseMode.Html, replyMarkup: new ReplyKeyboardRemove());
                return await GetQuestion(callbackQuery.Message);
            }
            case "start_testing":
            {
                return await StartSession(callbackQuery.Message);
            }
            case "generate_report":
            {
                return await GetReport(callbackQuery.Message, callbackQuery.Message.Chat.Id.ToString());
            }
            default:
            {
                if (parseData is not null)
                {
                    var message = await idccService.SendAnswerAsync(user.Id.ToString(), parseData.Value.Item1, parseData.Value.Item2,
                        _questionTime);

                    if (message is not null)
                    {
                        return await bot.SendTextMessageAsync(chat!, message.Message, parseMode: ParseMode.Html, replyMarkup: new ReplyKeyboardRemove());
                    }
                    
                    Thread.Sleep(500);
                    await bot.DeleteMessageAsync( chatId: chat!, messageId: callbackQuery.Message!.MessageId);
                    
                    logger.LogInformation("Ответ отправлен!");
                    var tMessage = await bot.SendTextMessageAsync(chat!, "Ответ отправлен!");
                    Thread.Sleep(2000);
                    await bot.DeleteMessageAsync( chatId: chat!, messageId: tMessage.MessageId);
                    
                    return await GetQuestion(callbackQuery.Message);
                }
                break;
            }
        }
        return await bot.SendTextMessageAsync(chat!, "Спасибо");
    }
    
    
    private Task UnknownUpdateHandlerAsync(Update update)
    {
        logger.LogInformation("Unknown update type: {UpdateType}", update.Type);
        return Task.CompletedTask;
    }

    (int, int)? ParseCallbackData(string data)
    {
        var regex = new Regex(_pattern);
        var match = regex.Match(data);
        if (!match.Success)
        {
            return null;
        }

        var questionId = Convert.ToInt32(match.Groups[1].Value);
        var answerId = Convert.ToInt32(match.Groups[2].Value);
        return (questionId, answerId);
    }
}