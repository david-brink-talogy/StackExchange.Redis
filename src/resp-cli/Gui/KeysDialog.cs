﻿using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using RESPite.Buffers;
using RESPite.Resp.Commands;
using Terminal.Gui;
using static RESPite.Resp.Commands.Type;
using Type = RESPite.Resp.Commands.Type;
namespace StackExchange.Redis.Gui;

internal class KeysDialog : ServerToolDialog
{
    private readonly TableView _keys;
    private readonly TextField _match;
    private readonly TextField _top;
    private readonly KeysRowSource _rows = new();
    private readonly ComboBox _type = new();
    private readonly ObservableCollection<string> _types = new() { "string", "list", "set", "zset", "hash", "stream" };

    private async Task FetchKeys()
    {
        try
        {
            if (!int.TryParse(_top.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                StatusText = "Invalid count: " + _top.Text;
            }

            var match = _match.Text;
            if (match is null or "*") match = "";

            int typeIndex = _type.SelectedItem;
            string? type = typeIndex < 0 ? null : _types[typeIndex];

            var cmd = new Scan(Match: Encoding.UTF8.GetBytes(match), Count: 100, Type: type);
            do
            {
                StatusText = $"Fetching next page...";
                using var reply = await Transport.SendAsync<Scan, Scan.Response>(cmd, CancellationToken);

                int start = _rows.Rows;
                int added = reply.Keys.ForEach(
                    static (i, span, state) =>
                    {
                        state.Add(Encoding.UTF8.GetString(span));
                        return true;
                    },
                    _rows);

                _keys.SetNeedsDisplay();

                StatusText = $"Fetching types...";

                for (int i = 0; i < added; i++)
                {
                    var obj = _rows[i];
                    obj.SetQueried();
                    using var key = LeasedBuffer.Utf8(obj.Key);
                    obj.SetType(await Transport.SendAsync<Type, KnownType>(new(key), CancellationToken));
                    _keys.SetNeedsDisplay();

                    switch (obj.Type)
                    {
                        case KnownType.String:
                            /*
                            obj.SetContent(await Transport.SendAsync())
                            */
                            break;
                    }
                }

                // update the cursor
                cmd = cmd.Next(reply);
            }
            while (cmd.Cursor != 0);
            StatusText = $"All done!";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    public KeysDialog()
    {
        Title = "Keys";
        StatusText = "original status";

        var topLabel = new Label()
        {
            Text = "Top",
        };
        _top = new()
        {
            X = Pos.Right(topLabel) + 1,
            Width = 8,
            Text = "100",
        };
        var typeLabel = new Label
        {
            Text = "Type",
            X = Pos.Right(_top) + 1,
        };
        _type = new()
        {
            X = Pos.Right(typeLabel) + 1,
            Width = 9,
        };
        _type.SetSource(_types);
        _type.CanFocus = true;
        var matchLabel = new Label()
        {
            Text = "Match",
            X = Pos.Right(_type) + 1,
        };
        _match = new()
        {
            X = Pos.Right(matchLabel) + 1,
            Width = Dim.Fill(10),
            Text = "*",
        };
        var btn = new Button()
        {
            X = Pos.Right(_match) + 1,
            Width = Dim.Fill(),
            Text = "Go",
        };
        btn.Accept += (s, e) => _ = FetchKeys();
        _keys = new TableView
        {
            Y = Pos.Bottom(matchLabel),
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Table = _rows,
        };

        Add(topLabel, _top, typeLabel, _type, matchLabel, _match, btn, _keys);
    }

    private sealed class KeysRowSource : ITableSource
    {
        private readonly List<KeysRow> _rows = new();

        public object this[int row, int col]
        {
            get
            {
                var obj = _rows[row];
                return col switch
                {
                    0 => obj.Key,
                    1 => obj.Type,
                    2 => obj.Content,
                    _ => throw new IndexOutOfRangeException(),
                };
            }
        }

        public string[] ColumnNames => ["Key", "Type", "Contents"];

        public int Columns => 3;

        public int Rows => _rows.Count;

        public void Add(string key) => _rows.Add(new(key));

        public KeysRow this[int index] => _rows[index];
    }

    public sealed class KeysRow(string key)
    {
        private int _state;
        public string Key => key;
        public KnownType Type { get; private set; } = KnownType.Unknown;

        public void SetQueried() => _state |= 0b001;
        public bool HaveQueried => (_state & 0b001) != 0;
        public bool HaveType => (_state & 0b010) != 0;
        public void SetType(KnownType type)
        {
            _state |= 0b010;
            Type = type;
        }
        public string Content { get; private set; } = "";

        public bool HaveContent => (_state & 0b100) != 0;

        public void SetContent(string content)
        {
            _state |= 0b100;
            Content = content;
        }
    }

    protected override async void OnStart()
    {
        try
        {
            StatusText = $"Querying database size...";
            var count = await Transport.SendAsync<DbSize, int>(CancellationToken);

            StatusText = $"Keys in database: {count}";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }
}
