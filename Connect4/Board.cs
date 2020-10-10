using System;

namespace Connect4
{
    [Serializable]
    public class Board
    {
        public static char CPU = 'C';
        public static char PLAYER = 'P';

        public int Height { get; } = 7;
        public int Width { get; } = 7;

        private char[,] _board;

        public Board()
        {
            _board = new char[Height, Width];
            InitializeEmpty();
        }

        public void PrintBoard()
        {
            for (int i = 0; i < Height; i++)
            {
                for (int j = 0; j < Width; j++)
                {
                    Console.Write(_board[i,j]);
                }

                Console.Write('\n');
            }
        }

        private void InitializeEmpty()
        {
            for (int i = 0; i < Height; i++)
            {
                for (int j = 0; j < Width; j++)
                {
                    _board[i, j] = '=';
                }
            }
        }

        public void UndoMove(int column)
        {
            //search and delete the first spot that is not free in a given column
            for (int i = 0; i < Height; i++)
            {
                if (_board[i, column] != '=')
                {
                    _board[i, column] = '=';
                    return;
                }
            }
        }

        public bool Move(int column, char player)
        {
            if (!MoveLegal(column))
            {
                return false;
            }

            //bottom - up search for the first open slot
            for (int i = Height - 1; i >= 0; i--)
            {
                if (_board[i, column] == '=')
                {
                    _board[i, column] = player;
                    return true;
                }
            }

            return false;
        }

        public bool GameEnd(int column, out char winner)
        {
            var lastInsertedRow = 0;

            for (int i = 0; i < Height; i++)
            {
                if (_board[i, column] != '=')
                {
                    lastInsertedRow = i;
                    break;
                }
            }

            var player = _board[lastInsertedRow, column];

            //check if player won horizontally
            var won = CheckForEndHorizontal(lastInsertedRow, player);
            if (won)
            {
                winner = player;
                return true;
            }

            //check if player won vertically
            won = CheckForEndVertical(column, player);
            if (won)
            {
                winner = player;
                return true;
            }

            //check if player won /-ly
            won = CheckForEndRightDiagonal(column, lastInsertedRow, player);
            if (won)
            {
                winner = player;
                return true;
            }

            //check if player won \-ly
            won = CheckForEndLeftDiagonal(column, lastInsertedRow, player);
            if (won)
            {
                winner = player;
                return true;
            }

            winner = '0';
            return false;
        }

        private bool CheckForEndLeftDiagonal(int lastInsertedColumn, int lastInsertedRow, char player)
        {
            var fourInARow = 0;

            var rowIndex = lastInsertedRow;
            var columnIndex = lastInsertedColumn;

            //lower yourself and check left diagonal up
            while (rowIndex < Height - 1 && columnIndex < Width - 1)
            {
                rowIndex++;
                columnIndex++;
            }

            //check left diagonal up for 4-in-a-row
            while (rowIndex >= 0 && columnIndex >= 0)
            {
                if (_board[rowIndex, columnIndex] == player)
                {
                    fourInARow++;
                }
                else
                {
                    fourInARow = 0;
                }

                rowIndex--;
                columnIndex--;
            }

            if (fourInARow >= 4)
            {
                return true;
            }

            return false;
        }

        private bool CheckForEndRightDiagonal(int lastInsertedColumn, int lastInsertedRow, char player)
        {
            var fourInARow = 0;

            var rowIndex = lastInsertedRow;
            var columnIndex = lastInsertedColumn;

            //lower yourself and check right diagonal up
            while (rowIndex < Height - 1 && columnIndex > 0)
            {
                rowIndex++;
                columnIndex--;
            }

            //check right diagonal up for 4-in-a-row
            while (rowIndex >= 0 && columnIndex <= Width - 1)
            {
                if (_board[rowIndex, columnIndex] == player)
                {
                    fourInARow++;
                }
                else
                {
                    fourInARow = 0;
                }

                rowIndex--;
                columnIndex++;
            }

            if (fourInARow >= 4)
            {
                return true;
            }

            return false;
        }

        private bool CheckForEndVertical(int lastInsertedColumn, char player)
        {
            var fourInARow = 0;

            for (int i = 0; i < Height; i++)
            {
                if (_board[i, lastInsertedColumn] == player)
                {
                    fourInARow++;
                }
                else
                {
                    fourInARow = 0;
                }
            }

            if (fourInARow >= 4)
            {
                return true;
            }

            return false;
        }

        private bool CheckForEndHorizontal(int lastInsertedRow, char player)
        {
            var fourInARow = 0;

            for (int i = 0; i < Width; i++)
            {
                if (_board[lastInsertedRow, i] == player)
                {
                    fourInARow++;
                }
                else
                {
                    fourInARow = 0;
                }
            }

            if (fourInARow >= 4)
            {
                return true;
            }

            return false;
        }

        public bool MoveLegal(int column)
        {
            if (column >= Width || column < 0 || _board[0, column] != '=')
            {
                return false;
            }

            return true;
        }
    }
}