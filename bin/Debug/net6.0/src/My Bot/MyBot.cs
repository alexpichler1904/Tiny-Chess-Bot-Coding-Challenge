﻿using ChessChallenge.API;
using System;
using System.Linq;

public class MyBot : IChessBot
{
    // bestMove gets updated everytime plyFromRoot is 0 and a new best move is found.
    Move bestMove;
    Board board;
    Timer timer;

    static ulong TTSize = 10000000;

    /*
    The transposititin table stores already evaluated positions and the corresponding best move.
    The index of a position in the array is its Hash % TTSize.
    Key is the full Hash of the position to detect Index Collisions,
    Depth the depth to which the position has been searched.
    NodeType is either 0 (lower Bound), 1 (exact Evaluation) or 2 (upper Bound).
    */
    (ulong key, int eval, Move move, int depth, byte nodeType)[] TranspositionTable = new (ulong, int, Move, int, byte)[TTSize];

    static int[] PawnSquareTable = {
			 0,   0,   0,   0,   0,   0,   0,   0,
			50,  50,  50,  50,  50,  50,  50,  50,
			10,  10,  20,  30,  30,  20,  10,  10,
			 5,   5,  10,  25,  25,  10,   5,   5,
			 0,   0,   0,  20,  20,   0,   0,   0,
			 5,  -5, -10,   0,   0, -10,  -5,   5,
			 5,  10,  10, -20, -20,  10,  10,   5,
			 0,   0,   0,   0,   0,   0,   0,   0
	};

    static int[] KnightSquareTable = {
			-50,-40,-30,-30,-30,-30,-40,-50,
			-40,-20,  0,  0,  0,  0,-20,-40,
			-30,  0, 10, 15, 15, 10,  0,-30,
			-30,  5, 15, 20, 20, 15,  5,-30,
			-30,  0, 15, 20, 20, 15,  0,-30,
			-30,  5, 10, 15, 15, 10,  5,-30,
			-40,-20,  0,  5,  5,  0,-20,-40,
			-50,-40,-30,-30,-30,-30,-40,-50,
	};
    // This Method gets called.
    public Move Think(Board board, Timer timer)
    {
        this.board = board;
        this.timer = timer;

        // Uses iterative deepening, so searches the position fully do a depth of 1, then 2, etc.
        int searchDepth = 1;

        // Start a new iterative deepening iteration when less than a 150th of the remaining time has gone by,
        // so one full search takes between a 150th and like a 10th of the remaining time.
        while (timer.MillisecondsElapsedThisTurn < timer.MillisecondsRemaining/150)
        Search(searchDepth++, 0, -999999, 999999);
        return bestMove;
    }

    int Search(int plyRemaining, int plyFromRoot, int alpha, int beta)
    {
        if(board.IsDraw()) return 0;

        int evalTT;
        Move moveTT;

        // LookupEvaluationTT returns true if there is a valid entry.
        if (LookupEvaluationTT(plyRemaining, plyFromRoot, alpha, beta, out evalTT, out moveTT))
        {
            if (plyFromRoot == 0) bestMove = moveTT;
            return evalTT;
        }

        if (plyRemaining == 0) return QuiescenceSearch(alpha, beta);

        if (board.IsInCheckmate()) return -(99999 - plyFromRoot);

        Move[] moves = board.GetLegalMoves();

        // It does not matter when the Transposition Table returns an Index Collision or null since OrderMoves will discard it.
        Move prevBestMove = plyFromRoot == 0 ? bestMove : TranspositionTable[board.ZobristKey % TTSize].move;
        OrderMoves(moves, prevBestMove);

        // Store the position in the Transposition table with the nodeType 2 (upper Bound) if no position
        // better than alpha is found, otherwise the position gets stored with nodeType 1 (exact Evaluation).
        byte nodeType = 2;
        Move bestMoveInThisPosition = Move.NullMove;

        foreach (Move move in moves)
        {
            board.MakeMove (move);
            int eval = -Search(plyRemaining - 1 + (board.IsInCheck() ? 1 : 0), plyFromRoot + 1, -beta, -alpha);
            board.UndoMove (move);
            
            if (eval >= beta)
            {
                // Stores the position with nodeType 0 (lower Bound) because the position could be a lot better than beta.
                StoreEvaluationTT(beta, move, plyRemaining, plyFromRoot, 0);
                return beta;
            }
            if (eval > alpha)
            {
                alpha = eval;

                nodeType = 1;
                bestMoveInThisPosition = move;

                if (plyFromRoot == 0) bestMove = move;
            }
        }

        StoreEvaluationTT(alpha, bestMoveInThisPosition, plyRemaining, plyFromRoot, nodeType);

        return alpha;
    }

    int QuiescenceSearch(int alpha, int beta)
    {
        // Evaluate the current position to prevent a position from looking like a bad one where you only have bad capturing moves.
        // There is usually a non capturing move that is at least as good as the static position.
        int eval = Evaluate();
        if (eval >= beta) return beta;
        if (eval > alpha) alpha = eval;

        Move[] moves = board.GetLegalMoves(true);
        OrderMoves(moves, Move.NullMove);

        foreach (Move move in moves)
        {
            board.MakeMove (move);
            eval = -QuiescenceSearch (-beta, -alpha);
            board.UndoMove(move);
            if (eval >= beta) return beta;
            if (eval > alpha) alpha = eval;
        }
        return alpha;

        
    }

    void OrderMoves(Move[] moves, Move hashMove)
    {
        // Creates an array of tuples with all moves and their associated values.
        var moveScores = new (Move, int)[moves.Length];
        
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            int score = 0;

            // Moves are primarily ranked higher the higher the value of the captured piece, and then lower
            // the lower the value of the capturing piece.
            if (move.CapturePieceType != PieceType.None) score += 10 * GetPieceValue(move.CapturePieceType) - GetPieceValue(move.MovePieceType);
            
            if (move.IsPromotion) score += GetPieceValue (move.PromotionPieceType);
            
            // It doesn't matter when the hashMove is null or doesn't exist in this position
            if (move.Equals(hashMove)) score = 99999;

            moveScores[i] = (move, score);
        }

        // Order the moves with their scores and then extract an array with only the ordered moves.
        var orderedMoveScores = moveScores.OrderByDescending(moveScore => moveScore.Item2).ToArray();

        for(int i = 0; i < moves.Length; i++)
        {
            moves[i] = orderedMoveScores[i].Item1;
        }
    }

    int Evaluate()
    {
        int eval = 0;

        foreach (PieceList pieceList in board.GetAllPieceLists())
        {
            PieceType pieceType = pieceList.TypeOfPieceInList;
            int pieceValue = GetPieceValue(pieceType);
           
            eval += pieceList.Count * pieceValue * (pieceList.IsWhitePieceList ? 1 : -1);

            int[] squareTable;
            if (pieceType == PieceType.Pawn) squareTable = PawnSquareTable;
            else if (pieceType == PieceType.Knight) squareTable = KnightSquareTable;
            else continue;

            foreach(Piece piece in pieceList)
            {
                if (piece.IsWhite) eval += squareTable[63 - piece.Square.Index]; 
                else eval -= squareTable[piece.Square.Index];
            }
            
        }
        return eval * (board.IsWhiteToMove ? 1 : -1);
    }

    int GetPieceValue (PieceType pieceType)
    {
        return (int)pieceType switch{
            1 => 100,
            2 => 300,
            3 => 330,
            4 => 500,
            5 => 900,
            6 => 0,
        };
    }

    bool LookupEvaluationTT(int depth, int plyFromRoot, int alpha, int beta, out int eval, out Move move)
    {
        var entry = TranspositionTable[board.ZobristKey % TTSize];

        eval = entry.eval;
        move = entry.move;

        // Make sure there is no Index Collision and the position has been searched deep enough.
        if (entry.key == board.ZobristKey && entry.depth >= depth)
        {
            // Decrease the value of a mating sequence if the calling method is deep in the search tree.
            if (Math.Abs(eval) > 90000)
            {
                int sign = Math.Sign(eval);
                eval = (eval * sign - plyFromRoot) * sign; 
            }
            if (entry.nodeType == 1) return true;

            // We stored the upper Bound of the eval of this position.
            // If the stored eval is less than the current alpha, we don't need to search this position
            // because we know it is surely worse than our current best option (alpha).
            if (entry.nodeType == 2 && eval <= alpha) return true;

            // We stored the lower Bound of the eval of this position.
            // If it causes a beta-cutoff anyway, we don't need to search this position
            // because it doesn't matter how much better is is than beta.
            if (entry.nodeType == 0 && eval >= beta) return true;
        }
        return false;
    }

    void StoreEvaluationTT(int eval, Move move, int depth, int plyFromRoot, byte nodeType)
    {
        // Store a mating sequence with a value as if it is found at depth 0.
        if (Math.Abs(eval) > 90000)
        {
            int sign = Math.Sign(eval);
            eval = (eval * sign + plyFromRoot) * sign;
        }
        TranspositionTable[board.ZobristKey % TTSize] =
        (board.ZobristKey, eval, move, depth, nodeType);
    }
}

