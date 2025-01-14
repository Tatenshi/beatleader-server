﻿using System;
using BeatLeader_Server.Models;
public enum ScoringType
{
    Default,
    Ignore,
    NoScore,
    Normal,
    SliderHead,
    SliderTail,
    BurstSliderHead,
    BurstSliderElement
}

public class NoteParams
{
    public ScoringType scoringType;
    public int lineIndex;
    public int noteLineLayer;
    public int colorType;
    public int cutDirection;

    public NoteParams(int noteId)
    {
        int id = noteId;
        if (id < 100000) {
            scoringType = (ScoringType)(id / 10000);
            id -= (int)scoringType * 10000;

            lineIndex = id / 1000;
            id -= lineIndex * 1000;

            noteLineLayer = id / 100;
            id -= noteLineLayer * 100;

            colorType = id / 10;
            cutDirection = id - colorType * 10;
        } else {
            scoringType = (ScoringType)(id / 10000000);
            id -= (int)scoringType * 10000000;

            lineIndex = id / 1000000;
            id -= lineIndex * 1000000;

            noteLineLayer = id / 100000;
            id -= noteLineLayer * 100000;

            colorType = id / 10;
            cutDirection = id - colorType * 10;
        }
    }
}

namespace BeatLeader_Server.Utils
{
    class NoteStruct
    {
        public int score;
        public bool isBlock;
        public float time;
        public ScoringType scoringType;

        public float multiplier;
        public int totalScore;
        public float accuracy;
        public int combo;
    }

    class MultiplierCounter
    {
        public int Multiplier { get; private set; } = 1;

        private int _multiplierIncreaseProgress;
        private int _multiplierIncreaseMaxProgress = 2;

        public void Reset()
        {
            Multiplier = 1;
            _multiplierIncreaseProgress = 0;
            _multiplierIncreaseMaxProgress = 2;
        }

        public void Increase()
        {
            if (Multiplier >= 8) return;

            if (_multiplierIncreaseProgress < _multiplierIncreaseMaxProgress)
            {
                ++_multiplierIncreaseProgress;
            }

            if (_multiplierIncreaseProgress >= _multiplierIncreaseMaxProgress)
            {
                Multiplier *= 2;
                _multiplierIncreaseProgress = 0;
                _multiplierIncreaseMaxProgress = Multiplier * 2;
            }
        }

        public void Decrease()
        {
            if (_multiplierIncreaseProgress > 0)
            {
                _multiplierIncreaseProgress = 0;
            }

            if (Multiplier > 1)
            {
                Multiplier /= 2;
                _multiplierIncreaseMaxProgress = Multiplier * 2;
            }
        }
    }

    class ReplayStatisticUtils
    {
        public static ScoreStatistic ProcessReplay(Replay replay, Leaderboard leaderboard)
        {
            ScoreStatistic result = new ScoreStatistic();
            float firstNoteTime = replay.notes.FirstOrDefault()?.eventTime ?? 0.0f;
            float lastNoteTime = replay.notes.LastOrDefault()?.eventTime ?? 0.0f;
            result.winTracker = new WinTracker
            {
                won = replay.info.failTime < 0.01,
                endTime = (replay.frames.LastOrDefault() != null) ? replay.frames.Last().time : 0,
                nbOfPause = replay.pauses.Where(p => p.time >= firstNoteTime && p.time <= lastNoteTime).Count(),
                jumpDistance = replay.info.jumpDistance,
                averageHeight = replay.heights.Count() > 0 ? replay.heights.Average(h => h.height) : replay.info.height,
                averageHeadPosition = new AveragePosition {
                    x = replay.frames.Average(f => f.head.position.x),
                    y = replay.frames.Average(f => f.head.position.y),
                    z = replay.frames.Average(f => f.head.position.z),
                }
            };

            HitTracker hitTracker = new HitTracker();
            result.hitTracker = hitTracker;

            foreach (var item in replay.notes)
            {
                NoteParams param = new NoteParams(item.noteID);
                switch (item.eventType)
                {
                    case NoteEventType.bad:
                        if (item.noteCutInfo.saberType == 0)
                        {
                            hitTracker.leftBadCuts++;
                        }
                        else
                        {
                            hitTracker.rightBadCuts++;
                        }
                        break;
                    case NoteEventType.miss:
                        if (param.colorType == 0)
                        {
                            hitTracker.leftMiss++;
                        }
                        else
                        {
                            hitTracker.rightMiss++;
                        }
                        break;
                    case NoteEventType.bomb:
                        if (param.colorType == 0)
                        {
                            hitTracker.leftBombs++;
                        }
                        else
                        {
                            hitTracker.rightBombs++;
                        }
                        break;
                    default:
                        break;
                }
            }
            (AccuracyTracker accuracy, List<NoteStruct> structs, int maxCombo) = Accuracy(replay);
            result.hitTracker.maxCombo = maxCombo;
            result.winTracker.totalScore = structs.Last().totalScore;
            result.accuracyTracker = accuracy;
            result.scoreGraphTracker = ScoreGraph(structs, (int)replay.frames.Last().time);

            return result;
        }

        public static (AccuracyTracker, List<NoteStruct>, int) Accuracy(Replay replay)
        {
            AccuracyTracker result = new AccuracyTracker();
            result.gridAcc = new List<float>(new float[12]);
            result.leftAverageCut = new List<float>(new float[3]);
            result.rightAverageCut = new List<float>(new float[3]);

            int[] gridCounts = new int[12];
            int[] leftCuts = new int[3];
            int[] rightCuts = new int[3];

            List<NoteStruct> allStructs = new List<NoteStruct>();
            foreach (var note in replay.notes)
            {
                NoteParams param = new NoteParams(note.noteID);
                int scoreValue = ScoreForNote(note, param.scoringType);

                if (scoreValue > 0)
                {
                    int index = param.noteLineLayer * 4 + param.lineIndex;
                    if (index > 11 || index < 0) {
                        index = 0;
                    }

                    if (param.scoringType != ScoringType.BurstSliderElement
                     && param.scoringType != ScoringType.BurstSliderHead)
                    {
                        gridCounts[index]++;
                        result.gridAcc[index] += (float)scoreValue;
                    }

                    (int before, int after, int acc) = CutScoresForNote(note, param.scoringType);
                    if (param.colorType == 0)
                    {
                        if (param.scoringType != ScoringType.SliderTail && param.scoringType != ScoringType.BurstSliderElement) {
                            result.leftAverageCut[0] += (float)before;
                            result.leftPreswing += note.noteCutInfo.beforeCutRating;
                            leftCuts[0]++;
                        }
                        if (param.scoringType != ScoringType.BurstSliderElement
                         && param.scoringType != ScoringType.BurstSliderHead)
                        {
                            result.leftAverageCut[1] += (float)acc;
                            result.accLeft += (float)scoreValue;
                            result.leftTimeDependence += Math.Abs(note.noteCutInfo.cutNormal.z);
                            leftCuts[1]++;
                        }
                        if (param.scoringType != ScoringType.SliderHead
                            && param.scoringType != ScoringType.BurstSliderHead
                            && param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.leftAverageCut[2] += (float)after;
                            result.leftPostswing += note.noteCutInfo.afterCutRating;
                            leftCuts[2]++;
                        }
                    }
                    else
                    {
                        if (param.scoringType != ScoringType.SliderTail && param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.rightAverageCut[0] += (float)before;
                            result.rightPreswing += note.noteCutInfo.beforeCutRating;
                            rightCuts[0]++;
                        }
                        if (param.scoringType != ScoringType.BurstSliderElement 
                         && param.scoringType != ScoringType.BurstSliderHead)
                        {
                            result.rightAverageCut[1] += (float)acc;
                            result.rightTimeDependence += Math.Abs(note.noteCutInfo.cutNormal.z);
                            result.accRight += (float)scoreValue;
                            rightCuts[1]++;
                        }
                        if (param.scoringType != ScoringType.SliderHead
                            && param.scoringType != ScoringType.BurstSliderHead
                            && param.scoringType != ScoringType.BurstSliderElement)
                        {
                            result.rightAverageCut[2] += (float)after;
                            result.rightPostswing += note.noteCutInfo.afterCutRating;
                            rightCuts[2]++;
                        }
                    }
                }

                allStructs.Add(new NoteStruct
                {
                    time = note.eventTime,
                    isBlock = param.colorType != 2,
                    score = scoreValue,
                    scoringType = param.scoringType,
                });
            }

            foreach (var wall in replay.walls)
            {
                allStructs.Add(new NoteStruct
                {
                    time = wall.time,

                    score = -5
                });
            }

            for (int i = 0; i < result.gridAcc.Count(); i++)
            {
                if (gridCounts[i] > 0)
                {
                    result.gridAcc[i] /= (float)gridCounts[i];
                }
            }

            if (leftCuts[0] > 0)
            {
                result.leftAverageCut[0] /= (float)leftCuts[0];
                result.leftPreswing /= (float)leftCuts[0];
            }

            if (leftCuts[1] > 0)
            {
                result.leftAverageCut[1] /= (float)leftCuts[1];

                result.accLeft /= (float)leftCuts[1];
                result.leftTimeDependence /= (float)leftCuts[1];
            }

            if (leftCuts[2] > 0)
            {
                result.leftAverageCut[2] /= (float)leftCuts[2];

                result.leftPostswing /= (float)leftCuts[2];
            }

            if (rightCuts[0] > 0)
            {
                result.rightAverageCut[0] /= (float)rightCuts[0];
                result.rightPreswing /= (float)rightCuts[0];
            }

            if (rightCuts[1] > 0)
            {
                result.rightAverageCut[1] /= (float)rightCuts[1];

                result.accRight /= (float)rightCuts[1];
                result.rightTimeDependence /= (float)rightCuts[1];
            }

            if (rightCuts[2] > 0)
            {
                result.rightAverageCut[2] /= (float)rightCuts[2];

                result.rightPostswing /= (float)rightCuts[2];
            }

            allStructs = allStructs.OrderBy(s => s.time).ToList();

            int multiplier = 1;
            int score = 0, noteIndex = 0;
            int combo = 0, maxCombo = 0;
            int maxScore = 0;
            MultiplierCounter maxCounter = new MultiplierCounter();
            MultiplierCounter normalCounter = new MultiplierCounter();

            for (var i = 0; i < allStructs.Count(); i++)
            {
                var note = allStructs[i];
                int scoreForMaxScore = 115;
                if (note.scoringType == ScoringType.BurstSliderHead) {
                    scoreForMaxScore = 85;
                } else if (note.scoringType == ScoringType.BurstSliderElement) {
                    scoreForMaxScore = 20;
                }
                maxCounter.Increase();
                maxScore += maxCounter.Multiplier * scoreForMaxScore;

                if (note.score < 0)
                {
                    normalCounter.Decrease();
                    multiplier = normalCounter.Multiplier;
                    combo = 0;
                }
                else
                {
                    normalCounter.Increase();
                    combo++;
                    multiplier = normalCounter.Multiplier;
                    score += multiplier * note.score;
                }

                if (combo > maxCombo)
                {
                    maxCombo = combo;
                }

                note.multiplier = multiplier;
                note.totalScore = score;
                note.combo = combo;

                if (note.isBlock)
                {
                    note.accuracy = (float)note.totalScore / maxScore;
                    noteIndex++;
                }
                else
                {
                    note.accuracy = i == 0 ? 0 : allStructs[i - 1].accuracy;
                }
            }

            return (result, allStructs, maxCombo);
        }

        public static ScoreGraphTracker ScoreGraph(List<NoteStruct> structs, int replayLength)
        {
            ScoreGraphTracker scoreGraph = new ScoreGraphTracker();
            scoreGraph.graph = new List<float>(new float[replayLength]);

            int structIndex = 0;

            for (int i = 0; i < replayLength; i++)
            {
                float cumulative = 0.0f;
                int delimiter = 0;
                while (structIndex < structs.Count() && structs[structIndex].time < i + 1)
                {
                    cumulative += structs[structIndex].accuracy;
                    structIndex++;
                    delimiter++;
                }
                if (delimiter > 0)
                {
                    scoreGraph.graph[i] = cumulative / (float)delimiter;
                }
                if (scoreGraph.graph[i] == 0)
                {
                    scoreGraph.graph[i] = i == 0 ? 1.0f : scoreGraph.graph[i - 1];
                }
            }

            return scoreGraph;
        }

        public static float Clamp(float value)
        {
            if (value < 0.0) return 0.0f;
            return value > 1.0f ? 1.0f : value;
        }

        public static int ScoreForNote(NoteEvent note, ScoringType scoringType)
        {
            if (note.eventType == NoteEventType.good)
            {
                (int before, int after, int acc) = CutScoresForNote(note, scoringType);

                return before + after + acc;
            }
            else
            {
                switch (note.eventType)
                {
                    case NoteEventType.bad:
                        return -2;
                    case NoteEventType.miss:
                        return -3;
                    case NoteEventType.bomb:
                        return -4;
                }
            }
            return -1;
        }

        public static (int, int, int) CutScoresForNote(NoteEvent note, ScoringType scoringType)
        {
            var cut = note.noteCutInfo;
            double beforeCutRawScore = 0;
            if (scoringType != ScoringType.BurstSliderElement)
            {
                if (scoringType == ScoringType.SliderTail)
                {
                    beforeCutRawScore = 70;
                }
                else
                {
                    beforeCutRawScore = Math.Clamp(Math.Round(70 * cut.beforeCutRating), 0, 70);
                }
            }
            double afterCutRawScore = 0;
            if (scoringType != ScoringType.BurstSliderElement)
            {
                if (scoringType == ScoringType.BurstSliderHead)
                {
                    afterCutRawScore = 0;
                }
                else if (scoringType == ScoringType.SliderHead)
                {
                    afterCutRawScore = 30;
                }
                else
                {
                    afterCutRawScore = Math.Clamp(Math.Round(30 * cut.afterCutRating), 0, 30);
                }
            }
            double cutDistanceRawScore = 0;
            if (scoringType == ScoringType.BurstSliderElement)
            {
                cutDistanceRawScore = 20;
            }
            else
            {

                double num = 1 - Clamp(cut.cutDistanceToCenter / 0.3f);
                cutDistanceRawScore = Math.Round(15 * num);

            }

            return ((int)beforeCutRawScore, (int)afterCutRawScore, (int)cutDistanceRawScore);
        }

        public static List<float> AverageList(List<List<float>> total) {
            int length = total.Max(t => t.Count);
            var result = new List<float>(length);
            for (int i = 0; i < length; i++)
            {
                float sum = 0;
                float count = 0;
                for (int j = 0; j < total.Count; j++)
                {
                    if (i < total[j].Count) {
                        sum += total[j][i];
                        count++;
                    }
                }
                result.Add(count > 0 ? sum / count : 0);
            }
            return result;

        }

        public static async Task AverageStatistic(IEnumerable<Task<ScoreStatistic>> statisticsAsync, ScoreStatistic leaderboardStatistic) {
            var statistics = (await Task.WhenAll(statisticsAsync)).Where(st => st != null).ToList();


            leaderboardStatistic.winTracker = new WinTracker {
                won = statistics.Average(st => st.winTracker.won ? 1.0 : 0.0) > 0.5,
                endTime = statistics.Average(st => st.winTracker.endTime),
                nbOfPause = (int)Math.Round(statistics.Average(st => st.winTracker.nbOfPause)),
                jumpDistance = statistics.Average(st => st.winTracker.jumpDistance),
                averageHeight = statistics.Average(st => st.winTracker.averageHeight),
                totalScore = (int)statistics.Average(st => st.winTracker.totalScore)
            };

            leaderboardStatistic.hitTracker = new HitTracker {
                maxCombo = (int)Math.Round(statistics.Average(st => st.hitTracker.maxCombo)),
                leftMiss = (int)Math.Round(statistics.Average(st => st.hitTracker.leftMiss)),
                rightMiss = (int)Math.Round(statistics.Average(st => st.hitTracker.rightMiss)),
                leftBadCuts = (int)Math.Round(statistics.Average(st => st.hitTracker.leftBadCuts)),
                rightBadCuts = (int)Math.Round(statistics.Average(st => st.hitTracker.rightBadCuts)),
                leftBombs = (int)Math.Round(statistics.Average(st => st.hitTracker.leftBombs)),
                rightBombs = (int)Math.Round(statistics.Average(st => st.hitTracker.rightBombs))
            };

            leaderboardStatistic.accuracyTracker = new AccuracyTracker {
                accRight = statistics.Average(st => st.accuracyTracker.accRight),
                accLeft = statistics.Average(st => st.accuracyTracker.accLeft),
                leftPreswing = statistics.Average(st => st.accuracyTracker.leftPreswing),
                rightPreswing = statistics.Average(st => st.accuracyTracker.rightPreswing),
                averagePreswing = statistics.Average(st => st.accuracyTracker.averagePreswing),
                leftPostswing = statistics.Average(st => st.accuracyTracker.leftPostswing),
                rightPostswing = statistics.Average(st => st.accuracyTracker.rightPostswing),
                leftTimeDependence = statistics.Average(st => st.accuracyTracker.leftTimeDependence),
                rightTimeDependence = statistics.Average(st => st.accuracyTracker.rightTimeDependence),
                leftAverageCut = AverageList(statistics.Select(st => st.accuracyTracker.leftAverageCut).ToList()),
                rightAverageCut = AverageList(statistics.Select(st => st.accuracyTracker.rightAverageCut).ToList()),
                gridAcc = AverageList(statistics.Select(st => st.accuracyTracker.gridAcc).ToList())
            };

            leaderboardStatistic.scoreGraphTracker = new ScoreGraphTracker {
                graph = AverageList(statistics.Select(st => st.scoreGraphTracker.graph).ToList())
            };
        }
    }
}