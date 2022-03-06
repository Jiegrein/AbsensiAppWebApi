﻿using AbsensiAppWebApi.DB.Entities;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AbsensiAppWebApi.Services
{
    public class AdminService
    {
        public AbsensiAppDbContext Db { get; set; }
        private readonly string BlobRootPath;

        public AdminService(AbsensiAppDbContext dbcontext)
        {
            this.Db = dbcontext;
            this.BlobRootPath = Path.GetTempPath();
        }

        public async Task<List<WorkerLog>> GenerateExcelData(DateTime dateFrom, DateTime dateTo)
        {
            var query = await (from w in Db.WorkerLogs
                               where dateFrom <= w.CreatedAt && dateTo >= w.CreatedAt
                               select w)
                               .Include(Q => Q.Worker)
                               .OrderBy(Q => Q.Worker.Fullname)
                               .ThenBy(Q => Q.CreatedAt)
                               .ToListAsync();

            return query;
        }

        public async Task<string> CreateExcel(DateTime dateFrom, DateTime dateTo)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add();
            var query = await GenerateExcelData(dateFrom, dateTo);

            var workerThatExistInSelectedDate = query.Select(Q => Q.WorkerId).Distinct();

            var allWorkerData = await Db.Workers.Select(Q => Q).ToListAsync();

            var dateRangeDict = CreateWeeklyRange(dateFrom, dateTo);

            var row = 1;
            var firstTableAddress = worksheet.Cell(row, 1).Address;

            worksheet.Cell("A1").Value = new TimeSpan(8, 0, 0);
            worksheet.Cell("B1").Value = new TimeSpan(12, 0, 0);
            worksheet.Cell("C1").Value = new TimeSpan(13, 0, 0);
            worksheet.Cell("D1").Value = new TimeSpan(17, 0, 0);
            row++;

            foreach (var item in dateRangeDict.Keys)
            {
                var from = item;
                var exist = dateRangeDict.TryGetValue(from, out var to);
                var counter = 1;

                worksheet.Cell(row, 1).Value = from.ToString("dddd, dd MMMM yyyy", new CultureInfo("id-ID")) + " - " + to.ToString("dddd, dd MMMM yyyy", new CultureInfo("id-ID"));
                row += 2;

                worksheet.Column("G").Style.NumberFormat.SetNumberFormatId((int)XLPredefinedFormat.Number.IntegerWithSeparator);
                worksheet.Column("H").Style.NumberFormat.SetNumberFormatId((int)XLPredefinedFormat.Number.IntegerWithSeparator);
                worksheet.Column("I").Style.NumberFormat.SetNumberFormatId((int)XLPredefinedFormat.Number.IntegerWithSeparator);

                foreach (var workerId in workerThatExistInSelectedDate)
                {
                    var data = query.Where(Q => Q.WorkerId == workerId && Q.CreatedAt >= from && Q.CreatedAt <= to).ToList();

                    if (data.Count > 0)
                    {
                        var name = data.Select(Q => Q.Worker.Fullname).Distinct().FirstOrDefault();

                        worksheet.Cell(row, 1).Value = name;

                        var dailyPay = allWorkerData
                            .Where(Q => Q.Id == workerId)
                            .Select(Q => Q.DailyPay)
                            .FirstOrDefault();

                        var totalPay = 0m;

                        worksheet.Cell(row, 2).Value = "Tanggal kerja";
                        worksheet.Cell(row, 3).Value = "Masuk kerja";
                        worksheet.Cell(row, 4).Value = "Mulai istirahat";
                        worksheet.Cell(row, 5).Value = "Selesai istirahat";
                        worksheet.Cell(row, 6).Value = "Selesai kerja";
                        worksheet.Cell(row, 7).Value = "Gaji per hari";
                        worksheet.Cell(row, 8).Value = "Penalti keterlambatan";
                        worksheet.Cell(row, 9).Value = "Total gaji per hari";
                        row++;

                        foreach (var log in data)
                        {
                            var lateStartWork = Math.Ceiling((new DateTime(log.StartWork.GetValueOrDefault().Year, log.StartWork.GetValueOrDefault().Month, log.StartWork.GetValueOrDefault().Day, 8, 0, 0)
                                - log.StartWork.GetValueOrDefault()).TotalMinutes);
                            var earlyStartBreak = Math.Ceiling((new DateTime(log.StartBreak.GetValueOrDefault().Year, log.StartBreak.GetValueOrDefault().Month, log.StartBreak.GetValueOrDefault().Day, 12, 0, 0)
                                - log.StartBreak.GetValueOrDefault()).TotalMinutes);
                            var lateEndBreak = Math.Ceiling((new DateTime(log.EndBreak.GetValueOrDefault().Year, log.EndBreak.GetValueOrDefault().Month, log.EndBreak.GetValueOrDefault().Day, 13, 0, 0)
                                - log.EndBreak.GetValueOrDefault()).TotalMinutes);
                            var earlyEndWork = Math.Ceiling((new DateTime(log.EndWork.GetValueOrDefault().Year, log.EndWork.GetValueOrDefault().Month, log.EndWork.GetValueOrDefault().Day, 17, 0, 0)
                                - log.EndWork.GetValueOrDefault()).TotalMinutes);

                            var deductedPay = Convert.ToDecimal(1000 * Math.Pow(lateStartWork, 2)) + Convert.ToDecimal(1000 * Math.Pow(earlyStartBreak, 2))
                                              + Convert.ToDecimal(1000 * Math.Pow(lateEndBreak, 2)) + Convert.ToDecimal(1000 * Math.Pow(earlyEndWork, 2));

                            totalPay += deductedPay;

                            var timeStartWork = new TimeSpan(log.StartWork.Value.Hour, log.StartWork.Value.Minute, log.StartWork.Value.Second);
                            var timeStartBreak = new TimeSpan(log.StartBreak.Value.Hour, log.StartBreak.Value.Minute, log.StartBreak.Value.Second);
                            var timeEndBreak = new TimeSpan(log.EndBreak.Value.Hour, log.EndBreak.Value.Minute, log.EndBreak.Value.Second);
                            var timeEndWork = new TimeSpan(log.EndWork.Value.Hour, log.EndWork.Value.Minute, log.EndWork.Value.Second);

                            worksheet.Cell(row, 2).Value = log.CreatedAt.ToString("dddd, dd MMMM yyyy", new CultureInfo("id-ID"));
                            worksheet.Cell(row, 3).Value = log.StartWork.HasValue ? $"{timeStartWork}" : "";
                            worksheet.Cell(row, 4).Value = log.StartBreak.HasValue ? $"{timeStartBreak}" : "";
                            worksheet.Cell(row, 5).Value = log.EndBreak.HasValue ? $"{timeEndBreak}" : "";
                            worksheet.Cell(row, 6).Value = log.EndWork.HasValue ? $"{timeEndWork}" : "";

                            worksheet.Cell(row, 7).Value = dailyPay;
                            worksheet.Cell(row, 8).FormulaR1C1 = "ROUNDUP((RC[-5]-R1C1)*1440,0)";
                            worksheet.Cell(row, 8).FormulaR1C1 = "=ROUND((IF(ROUNDUP((RC[-5]-R1C1)*1440,0) > 5, ROUNDUP((RC[-5]-R1C1)*1440,0), 0) + IF(ROUNDUP((RC[-3]-R1C3)*1440,0) > 5, ROUNDUP((RC[-3]-R1C3)*1440,0), 0) + IF(ROUNDUP((RC[-2]-R1C4)*1440,0) < -5, -ROUNDUP((RC[-2]-R1C4)*1440,0), 0)) * (RC[-1] / 480), 0)";
                            worksheet.Cell(row, 9).FormulaR1C1 = "RC[-2]-RC[-1]";

                            row++;
                        }
                        // Display Totalpay to cell lower by 1 in the last deducted pay
                        worksheet.Range(row, 1, row, 8).Merge();
                        worksheet.Range(row, 1, row, 8).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                        worksheet.Cell(row, 1).Value = "Total Mingguan";

                        var dataCount = data.Count;

                        worksheet.Cell(row, 9).FormulaR1C1 = $"SUM(R[-{data.Count}]C:R[-1]C)";
                        row += 2;
                    }
                }
                counter++;
            }

            worksheet.Columns(1, 50).AdjustToContents();

            var lastUsedAddress = worksheet.Cell(row, 22).Address;
            var tableRange = worksheet.Range(firstTableAddress, lastUsedAddress);
            //tableRange.CreateTable();

            var fileName = $"Gajian tanggal {DateTime.Now:yyyyMMddHHmmss}.xlsx";

            var filePath = $"{BlobRootPath}/temp/excel/{fileName}";
            workbook.SaveAs(filePath);

            return filePath;
        }

        public Dictionary<DateTime, DateTime> CreateWeeklyRange(DateTime dateFrom, DateTime dateTo)
        {
            var from = dateFrom;
            var to = dateTo;

            var fromWeekStart = DateTime.Now;
            var toWeekEnd = DateTime.Now;

            var firstLoop = true;

            var dateDict = new Dictionary<DateTime, DateTime>();
            dateDict.Clear();

            // TODO add 4 days from monday for a faster loop
            while (from <= to)
            {
                if (firstLoop)
                {
                    if (from == to)
                    {
                        fromWeekStart = from;
                        toWeekEnd = from;

                        dateDict.Add(fromWeekStart, toWeekEnd);

                        // To exit while
                        from = from.AddDays(1);
                    }

                    if (from.DayOfWeek == DayOfWeek.Friday)
                    {
                        // If firstloop from date is friday, set fromWeekStart to from(1st date that was selected by user) and set toWeekEnd to the same date since we only care about Monday - Friday in the same week
                        fromWeekStart = from;
                        toWeekEnd = from.AddHours(23).AddMinutes(59).AddSeconds(59); ;

                        dateDict.Add(fromWeekStart, toWeekEnd);
                        // Add 3 days to move from into Monday
                        from = from.AddDays(3);
                    }
                    else
                    {
                        // Set fromWeekStart to the user selected From
                        fromWeekStart = from;
                        from = from.AddDays(1);
                    }
                    firstLoop = false;
                }
                else
                {
                    if (from == to)
                    {
                        if (from.DayOfWeek == DayOfWeek.Monday)
                        {
                            fromWeekStart = from;
                        }

                        toWeekEnd = from.AddHours(23).AddMinutes(59).AddSeconds(59); ;

                        dateDict.Add(fromWeekStart, toWeekEnd);

                        // To exit while
                        from = from.AddDays(1);
                    }

                    if (from.DayOfWeek == DayOfWeek.Monday)
                    {
                        // Set fromWeekStart to the user selected From
                        fromWeekStart = from;
                        from = from.AddDays(1);
                    }
                    else if (from.DayOfWeek == DayOfWeek.Friday)
                    {
                        // set toWeekEnd to Friday of that week since we only care about Monday - Friday in the same week
                        toWeekEnd = from.AddHours(23).AddMinutes(59).AddSeconds(59);

                        dateDict.Add(fromWeekStart, toWeekEnd);
                        // Add 3 days to move from into Monday
                        from = from.AddDays(3);

                    }
                    else
                    {
                        from = from.AddDays(1);
                    }
                }
            }

            return dateDict;
        }
    }
}