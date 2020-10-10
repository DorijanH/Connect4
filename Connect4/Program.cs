using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MPI;
using Environment = MPI.Environment;

namespace Connect4
{
    class Program
    {
        private const int MaxSearchLevel = 8;

        private static int _worldSize;
        private static int _myId;

        private const int BoardInitialization = 0;
        private const int TaskRequest = 1;
        private const int TaskInfo = 2;
        private const int TaskComplete = 3;
        private const int CalculationEnd = 4;
        private const int GameEnd = 5;

        private static Board _gameBoard = new Board();
        private static bool _gameEnd;

        //private static Stopwatch sw = new Stopwatch();

        static void Main(string[] args)
        {
            using (new Environment(ref args))
            {
                InitializeMPIValues();

                //Master
                if (_myId == 0)
                {
                    GameMaster();
                }

                //Slaves
                else
                {
                    Slave();
                }
            }
        }

        private static void Slave()
        {
            //slaves are running until the game is over
            while (!_gameEnd)
            {
                bool stepCalculationEnd = false;

                //we're waiting for either game ending tag or board initialization for the start of the next calculation
                var message = Communicator.world.Probe(0, Communicator.anyTag);
                if (message.Tag == GameEnd)
                {
                    Communicator.world.Receive<bool>(0, GameEnd);
                    _gameEnd = true;
                    break;
                }

                //first thing in each step is to synchronize gameboard - receive current board state from GameMaster
                var _gameBoard = Communicator.world.Receive<Board>(0, BoardInitialization);

                while (!stepCalculationEnd)
                {
                    //tell the GameMaster you're ready for a task
                    Communicator.world.Send(true, 0, TaskRequest);

                    //PrintMessage("Sent request for a task");

                    //wait for any message - could be a task or could be a calculation end
                    var incomingMessage = Communicator.world.Probe(0, Communicator.anyTag);

                    //if the message is calculation end
                    if (incomingMessage.Tag == CalculationEnd)
                    {
                        Communicator.world.Receive<bool>(0, CalculationEnd);
                        stepCalculationEnd = true;

                        //PrintMessage($"Received calculation end from {incomingMessage.Source}");
                        //PrintMessage($"Calculation ended");
                    }
                    //else, it's a new task
                    else
                    {
                        string newTask = Communicator.world.Receive<string>(0, TaskInfo);
                        int cpuMove = int.Parse(newTask.Split('-')[0]);
                        int playerMove = int.Parse(newTask.Split('-')[1]);

                        //PrintMessage($"Received task: CPU move - {CPUMove} Player move - {PlayerMove}");

                        double moveResult;
                        string response;

                        //if the CPU move is not legal return 0
                        if (!_gameBoard.MoveLegal(cpuMove))
                        {
                            //PrintMessage("Cpu move illegal");
                            response = FormatACompletionResponse(newTask, 0);
                            Communicator.world.Send(response, 0, TaskComplete);

                            //PrintMessage($"Sent {response} to 0");
                            continue;
                        }
                        //else, play the move
                        else
                        {
                            _gameBoard.Move(cpuMove, Board.CPU);
                        }

                        //if the player move is not legal return 0
                        if (!_gameBoard.MoveLegal(playerMove))
                        {
                            //PrintMessage("Player move illegal");
                            response = FormatACompletionResponse(newTask, 0);
                            Communicator.world.Send(response, 0, TaskComplete);

                            //PrintMessage($"Sent {response} to 0");

                            _gameBoard.UndoMove(cpuMove);
                            continue;
                        }
                        //else, play the move
                        else
                        {
                            _gameBoard.Move(playerMove, Board.PLAYER);
                        }

                        //after playing your task, evaluate it

                        //PrintMessage("Evaluating");
                        moveResult = Evaluate(_gameBoard, Board.PLAYER, playerMove, MaxSearchLevel - 2);
                        _gameBoard.UndoMove(playerMove);
                        _gameBoard.UndoMove(cpuMove);

                        response = FormatACompletionResponse(newTask, moveResult);

                        //send the result to the GameMaster and start requesting new task
                        Communicator.world.Send(response, 0, TaskComplete);

                        //PrintMessage($"Sent {response} to 0");
                    }
                }
            }
        }

        private static string FormatACompletionResponse(string newTask, double result)
        {
            //make a string that says which task was given and what result it got, ex. cpuMove-playerMove=result
            return $"{newTask}={result}";
        }

        private static void GameMaster()
        {
            while (!_gameEnd)
            {
                int userMove;

                //_gameBoard.PrintBoard();

                do
                {
                    userMove = int.Parse(Console.ReadLine());
                } while (!_gameBoard.MoveLegal(userMove));

                _gameBoard.Move(userMove, Board.PLAYER);

                if (_gameBoard.GameEnd(userMove, out char winner) && winner == Board.PLAYER)
                {
                    //_gameBoard.PrintBoard();
                    //Console.WriteLine("Game over! PLAYER WON!");

                    _gameEnd = true;
                    SendGameEnding();
                }

                //if user didn't win, it's our turn
                int cpuMove = CPUTurn();

                _gameBoard.Move(cpuMove, Board.CPU);
                _gameBoard.PrintBoard();
                
                //did we win
                if (_gameBoard.GameEnd(cpuMove, out char winner1) && winner1 == Board.CPU)
                {
                    _gameEnd = true;
                    //Console.WriteLine("Game over! CPU WON!");
                    SendGameEnding();
                }
            }
        }

        private static int CPUTurn()
        {
            //sw.Start();
            int nextMove;

            //if it's only me - I'll solve it myself 
            if (_worldSize == 1)
            {
                nextMove = SolveYourself();
            }
            //if there is/are others - make the calculation parallel
            else
            {
                nextMove = SolveInParallel();
            }

            //sw.Stop();

            //Console.WriteLine($"Calculating next move took: {sw.Elapsed.TotalMilliseconds} ms");

            //sw.Reset();
            return nextMove;
        }

        private static void SendCalculationEnd()
        {
            for (int i = 1; i < _worldSize; i++)
            {
                Communicator.world.Send(true, i, CalculationEnd);
            }
        }

        private static void SendGameEnding()
        {
            for (int i = 1; i < _worldSize; i++)
            {
                Communicator.world.Send(true, i, GameEnd);
            }
        }

        private static int ProcessTheResults(Dictionary<string, double> handedTaskCalculations)
        {
            double[] columnResults = new double[_gameBoard.Width];

            for (int cpuMove = 0; cpuMove < _gameBoard.Width; cpuMove++)
            {
                int possibleMoves = 0;

                if (!_gameBoard.MoveLegal(cpuMove)) continue;

                _gameBoard.Move(cpuMove, Board.CPU);

                //sum up the player moves - if there is a move that results in a player win (-1) immediately make that column result -1
                for (int playerMove = 0; playerMove < _gameBoard.Width; playerMove++)
                {
                    if (!_gameBoard.MoveLegal(playerMove)) continue;

                    possibleMoves++;

                    string taskName = $"{cpuMove}-{playerMove}";

                    if (handedTaskCalculations[taskName] == -1)
                    {
                        columnResults[cpuMove] = handedTaskCalculations[taskName];
                        break;
                    }
                    else
                    {
                        columnResults[cpuMove] += handedTaskCalculations[taskName];
                    }
                }

                _gameBoard.UndoMove(cpuMove);

                columnResults[cpuMove] /= possibleMoves;
            }

            foreach (var result in columnResults)
            {
                Console.Write($"{result.ToString("F3").Replace(",",".")} ");
            }

            double maxResult = columnResults.Max();
            int columnOfMaxResult = Array.IndexOf(columnResults, maxResult);

            Console.WriteLine();
            //Console.WriteLine($"Highest value = {maxResult:F3} on column: {columnOfMaxResult}");

            return columnOfMaxResult;
        }

        private static void ProcessTheRequest(Status incomingMessage, Dictionary<string, double> handedTaskCalculations, ref int calculationsCompleted)
        {
            //if it's a request for a new task - send new task
            if (incomingMessage.Tag == TaskRequest)
            {
                Communicator.world.Receive<bool>(incomingMessage.Source, TaskRequest);
                //PrintMessage($"Received a task request from {incomingMessage.Source}");

                string newTask = HandOutATask(handedTaskCalculations);

                if (newTask != string.Empty)
                {
                    Communicator.world.Send(newTask, incomingMessage.Source, TaskInfo);
                    //PrintMessage($"Sent task {newTask} to {incomingMessage.Source}");
                }
            }

            //if it's a completion of a task - save it and increment the completion counter
            if (incomingMessage.Tag == TaskComplete)
            {
                string received = Communicator.world.Receive<string>(incomingMessage.Source, TaskComplete);
                string task = received.Split('=')[0];
                double result = double.Parse(received.Split('=')[1]);

                //Console.WriteLine($"Received calculation for a task {task} from process {incomingMessage.Source} = {result}");

                //update the task calculations
                handedTaskCalculations[task] = result;

                calculationsCompleted++;

                //Console.WriteLine($"Current completed tasks = {calculationsCompleted}");
            }
        }

        private static string HandOutATask(Dictionary<string, double> handedTaskCalculations)
        {
            for (int cpuMove = 0; cpuMove < _gameBoard.Width; cpuMove++)
            {
                for (int playerMove = 0; playerMove < _gameBoard.Width; playerMove++)
                {
                    string taskName = $"{cpuMove}-{playerMove}";

                    //if we have not handed out this task already - create a new task ready to be calculated
                    if (!handedTaskCalculations.ContainsKey(taskName))
                    {
                        handedTaskCalculations.Add(taskName, 0);
                        return taskName;
                    }
                }
            }

            return string.Empty;
        }

        private static void SendGameBoard()
        {
            for (int i = 1; i < _worldSize; i++)
            {
                Communicator.world.Send(_gameBoard, i, BoardInitialization);
            }
        }

        private static int SolveYourself()
        {
            int bestColumn;
            double bestResult;
            var depth = MaxSearchLevel;

            do
            {
                bestColumn = -1;
                bestResult = -1;
                for (int i = 0; i < _gameBoard.Width; i++)
                {
                    if (_gameBoard.MoveLegal(i))
                    {
                        if (bestColumn == -1) bestColumn = i;

                        _gameBoard.Move(i, Board.CPU);
                        var result = Math.Round(Evaluate(_gameBoard, Board.CPU, i, depth - 1), 3);
                        _gameBoard.UndoMove(i);

                        if (result > bestResult)
                        {
                            //Console.WriteLine($"New best");
                            bestResult = result;
                            bestColumn = i;
                        }

                        //Console.WriteLine($"Column {i}, value: {result}");

                        Console.Write($"{result.ToString("F3").Replace(",",".")} ");
                    }
                }

                depth /= 2;
            } while (bestResult == -1 && depth > 0);

            Console.WriteLine();
            //Console.WriteLine($"The best column: {bestColumn}, value: {bestResult:F3}");

            return bestColumn;
            
        }

        private static int SolveInParallel()
        {
            int numberOfTasks = _gameBoard.Width * _gameBoard.Width;

            //first thing to do is get every slave up to speed - synchronize gameboard with everyone
            SendGameBoard();

            Dictionary<string, double> handedTaskCalculations = new Dictionary<string, double>(numberOfTasks);

            int calculationsCompleted = 0;

            //while there are still tasks that are not completed 
            while (calculationsCompleted < numberOfTasks)
            {
                //wait for any message
                var incomingMessage = Communicator.world.Probe(Communicator.anySource, Communicator.anyTag);

                ProcessTheRequest(incomingMessage, handedTaskCalculations, ref calculationsCompleted);
            }

            SendCalculationEnd();

            int move = ProcessTheResults(handedTaskCalculations);
            return move;
        }

        private static double Evaluate(Board currentBoard, char lastPlayer, int lastInsertedColumn, int currentDepth)
        {
            //3. rule helpers
            bool allWins = true;
            bool allLoses = true;

            //check if the game is over
            if (currentBoard.GameEnd(lastInsertedColumn, out char winner))
            {
                if (winner == Board.CPU) return 1;
                if (winner == Board.PLAYER) return -1;
            }

            //if there is no winner, and we are at the max depth, return 0
            if (currentDepth == 0) return 0;

            //if there are more depths to search, move to the next depth
            currentDepth--;

            char newPlayer = lastPlayer == Board.PLAYER ? Board.CPU : Board.PLAYER;
            double subNodesSum = 0;
            int possibleMoves = 0;

            for (int i = 0; i < currentBoard.Width; i++)
            {
                if (currentBoard.MoveLegal(i))
                {
                    possibleMoves++;
                    currentBoard.Move(i, newPlayer);
                    double subNodeResult = Evaluate(currentBoard, newPlayer, i, currentDepth);
                    currentBoard.UndoMove(i);

                    if (subNodeResult > -1) allLoses = false;
                    if (subNodeResult < 1) allWins = false;
                    if (subNodeResult >= 1 && newPlayer == Board.CPU) return 1;       //1. rule 
                    if (subNodeResult <= -1 && newPlayer == Board.PLAYER) return -1;  //2. rule

                    subNodesSum += subNodeResult;
                }
            }

            //3. rule
            if (allWins) return 1;
            if (allLoses) return -1;

            subNodesSum /= possibleMoves;
            return subNodesSum;
        }

        private static void InitializeMPIValues()
        {
            _worldSize = Communicator.world.Size;
            _myId = Communicator.world.Rank;
        }

        //private static void PrintMessage(string message)
        //{
        //    for (int i = 0; i < _myId; i++) Console.Write("\t");
        //    Console.WriteLine(message);
        //}
    }
}
