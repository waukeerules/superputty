using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace SuperPutty.Gui
{
    /// <summary>
    /// Turns SVG path data (the "d" attribute: M, L, H, V, C, S, Q, T, A and Z, absolute and relative) into a GDI+
    /// GraphicsPath, so BrandLogos can draw vector logos without an SVG library. Uses the non-zero fill rule, which is
    /// SVG's default.
    /// </summary>
    public static class SvgPath
    {
        /// <summary>Parse path data</summary>
        /// <param name="data">The path data, e.g. "M0 0h24v24H0z"</param>
        /// <returns>The path; the caller disposes it</returns>
        /// <exception cref="FormatException">The data has a command or number this doesn't understand</exception>
        public static GraphicsPath Parse(string data)
        {
            GraphicsPath path = new GraphicsPath(FillMode.Winding);
            Reader reader = new Reader(data);
            PointF current = PointF.Empty, start = PointF.Empty, lastControl = PointF.Empty;
            char command = ' ', previous = ' ';
            bool figureOpen = false;

            while (reader.SkipSeparators())
            {
                if (reader.AtCommand)
                {
                    command = reader.ReadCommand();
                }
                else if (command == 'M' || command == 'm')
                {
                    // numbers after a moveto are implicit linetos
                    command = command == 'M' ? 'L' : 'l';
                }
                else if (command == ' ' || command == 'Z' || command == 'z')
                {
                    throw new FormatException("Path data has numbers without a command");
                }

                bool relative = char.IsLower(command);
                PointF origin = relative ? current : PointF.Empty;
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        if (figureOpen)
                        {
                            path.StartFigure();
                        }
                        current = Offset(origin, reader.ReadPoint());
                        start = current;
                        figureOpen = true;
                        break;
                    case 'L':
                        current = LineTo(path, current, Offset(origin, reader.ReadPoint()));
                        break;
                    case 'H':
                        current = LineTo(path, current, new PointF((relative ? current.X : 0) + reader.ReadNumber(), current.Y));
                        break;
                    case 'V':
                        current = LineTo(path, current, new PointF(current.X, (relative ? current.Y : 0) + reader.ReadNumber()));
                        break;
                    case 'C':
                    {
                        PointF c1 = Offset(origin, reader.ReadPoint());
                        PointF c2 = Offset(origin, reader.ReadPoint());
                        PointF end = Offset(origin, reader.ReadPoint());
                        path.AddBezier(current, c1, c2, end);
                        lastControl = c2;
                        current = end;
                        break;
                    }
                    case 'S':
                    {
                        PointF c1 = "CcSs".IndexOf(previous) >= 0 ? Reflect(lastControl, current) : current;
                        PointF c2 = Offset(origin, reader.ReadPoint());
                        PointF end = Offset(origin, reader.ReadPoint());
                        path.AddBezier(current, c1, c2, end);
                        lastControl = c2;
                        current = end;
                        break;
                    }
                    case 'Q':
                    {
                        PointF control = Offset(origin, reader.ReadPoint());
                        PointF end = Offset(origin, reader.ReadPoint());
                        AddQuadratic(path, current, control, end);
                        lastControl = control;
                        current = end;
                        break;
                    }
                    case 'T':
                    {
                        PointF control = "QqTt".IndexOf(previous) >= 0 ? Reflect(lastControl, current) : current;
                        PointF end = Offset(origin, reader.ReadPoint());
                        AddQuadratic(path, current, control, end);
                        lastControl = control;
                        current = end;
                        break;
                    }
                    case 'A':
                    {
                        float rx = reader.ReadNumber(), ry = reader.ReadNumber(), rotation = reader.ReadNumber();
                        bool largeArc = reader.ReadFlag(), sweep = reader.ReadFlag();
                        PointF end = Offset(origin, reader.ReadPoint());
                        AddArc(path, current, rx, ry, rotation, largeArc, sweep, end);
                        current = end;
                        break;
                    }
                    case 'Z':
                        path.CloseFigure();
                        current = start;
                        figureOpen = false;
                        break;
                    default:
                        throw new FormatException("Unsupported path command '" + command + "'");
                }
                previous = command;
            }
            return path;
        }

        /// <summary>A point moved by an origin (for relative commands)</summary>
        private static PointF Offset(PointF origin, PointF p)
        {
            return new PointF(origin.X + p.X, origin.Y + p.Y);
        }

        /// <summary>A control point mirrored through the current point (for S and T)</summary>
        private static PointF Reflect(PointF control, PointF about)
        {
            return new PointF(2 * about.X - control.X, 2 * about.Y - control.Y);
        }

        /// <summary>Add a straight line and return its end</summary>
        private static PointF LineTo(GraphicsPath path, PointF from, PointF to)
        {
            path.AddLine(from, to);
            return to;
        }

        /// <summary>Add a quadratic curve as the equivalent cubic one, which is all GDI+ draws</summary>
        private static void AddQuadratic(GraphicsPath path, PointF from, PointF control, PointF to)
        {
            PointF c1 = new PointF(from.X + 2f / 3 * (control.X - from.X), from.Y + 2f / 3 * (control.Y - from.Y));
            PointF c2 = new PointF(to.X + 2f / 3 * (control.X - to.X), to.Y + 2f / 3 * (control.Y - to.Y));
            path.AddBezier(from, c1, c2, to);
        }

        /// <summary>
        /// Add an SVG elliptical arc as cubic curves: converts SVG's endpoint form to a centre and angles (SVG 1.1
        /// appendix F.6.5), then approximates each quarter or less with one curve
        /// </summary>
        private static void AddArc(GraphicsPath path, PointF from, float rx, float ry, float rotationDegrees,
            bool largeArc, bool sweep, PointF to)
        {
            if (from == to)
                return;
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx == 0 || ry == 0)
            {
                path.AddLine(from, to);
                return;
            }

            double phi = rotationDegrees * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (from.X - to.X) / 2.0, dy = (from.Y - to.Y) / 2.0;
            double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;

            // radii too small to reach the end point are scaled up, as SVG specifies
            double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            if (lambda > 1)
            {
                double scale = Math.Sqrt(lambda);
                rx = (float)(rx * scale);
                ry = (float)(ry * scale);
            }

            double numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            double denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            double factor = Math.Sqrt(Math.Max(0, numerator / denominator)) * (largeArc == sweep ? -1 : 1);
            double cx1 = factor * rx * y1 / ry, cy1 = -factor * ry * x1 / rx;
            double cx = cos * cx1 - sin * cy1 + (from.X + to.X) / 2.0;
            double cy = sin * cx1 + cos * cy1 + (from.Y + to.Y) / 2.0;

            double theta1 = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
            double delta = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
            if (!sweep && delta > 0)
            {
                delta -= 2 * Math.PI;
            }
            else if (sweep && delta < 0)
            {
                delta += 2 * Math.PI;
            }

            int segments = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2));
            double step = delta / segments;
            double k = 4.0 / 3 * Math.Tan(step / 4);
            PointF segmentStart = from;
            for (int i = 0; i < segments; i++)
            {
                double a1 = theta1 + i * step, a2 = a1 + step;
                PointF end = i == segments - 1 ? to : OnEllipse(cx, cy, rx, ry, cos, sin, a2);
                PointF c1 = Control(cx, cy, rx, ry, cos, sin, a1, k);
                PointF c2 = Control(cx, cy, rx, ry, cos, sin, a2, -k);
                path.AddBezier(segmentStart, c1, c2, end);
                segmentStart = end;
            }
        }

        /// <summary>The signed angle from one vector to another, in radians</summary>
        private static double Angle(double ux, double uy, double vx, double vy)
        {
            return Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        }

        /// <summary>A point on a rotated ellipse at an angle</summary>
        private static PointF OnEllipse(double cx, double cy, double rx, double ry, double cos, double sin, double angle)
        {
            double x = rx * Math.Cos(angle), y = ry * Math.Sin(angle);
            return new PointF((float)(cx + cos * x - sin * y), (float)(cy + sin * x + cos * y));
        }

        /// <summary>A Bezier control point for an arc segment: the point at the angle, moved along its tangent by k</summary>
        private static PointF Control(double cx, double cy, double rx, double ry, double cos, double sin, double angle, double k)
        {
            double x = rx * (Math.Cos(angle) - k * Math.Sin(angle));
            double y = ry * (Math.Sin(angle) + k * Math.Cos(angle));
            return new PointF((float)(cx + cos * x - sin * y), (float)(cy + sin * x + cos * y));
        }

        /// <summary>Reads commands, numbers and flags from path data, which may run numbers together ("1.5.5", "-1-2")</summary>
        private class Reader
        {
            private readonly string data;
            private int position;

            /// <summary>Read from path data</summary>
            /// <param name="data">The path data</param>
            public Reader(string data)
            {
                this.data = data ?? string.Empty;
            }

            /// <summary>Whether the next character is a command letter (E and e belong to numbers)</summary>
            public bool AtCommand
            {
                get
                {
                    char c = this.data[this.position];
                    return char.IsLetter(c) && c != 'e' && c != 'E';
                }
            }

            /// <summary>Skip spaces and commas</summary>
            /// <returns>false at the end of the data</returns>
            public bool SkipSeparators()
            {
                while (this.position < this.data.Length && (char.IsWhiteSpace(this.data[this.position]) || this.data[this.position] == ','))
                {
                    this.position++;
                }
                return this.position < this.data.Length;
            }

            /// <summary>Read a command letter</summary>
            /// <returns>The command</returns>
            public char ReadCommand()
            {
                return this.data[this.position++];
            }

            /// <summary>Read an x,y pair</summary>
            /// <returns>The point</returns>
            public PointF ReadPoint()
            {
                float x = this.ReadNumber();
                return new PointF(x, this.ReadNumber());
            }

            /// <summary>Read an arc flag, a single 0 or 1 that may be run together with what follows</summary>
            /// <returns>The flag</returns>
            /// <exception cref="FormatException">It's not 0 or 1</exception>
            public bool ReadFlag()
            {
                this.SkipSeparators();
                char c = this.position < this.data.Length ? this.data[this.position++] : ' ';
                if (c != '0' && c != '1')
                    throw new FormatException("Expected an arc flag at " + (this.position - 1));
                return c == '1';
            }

            /// <summary>Read a number</summary>
            /// <returns>The number</returns>
            /// <exception cref="FormatException">There's no number here</exception>
            public float ReadNumber()
            {
                this.SkipSeparators();
                int begin = this.position;
                if (this.position < this.data.Length && (this.data[this.position] == '-' || this.data[this.position] == '+'))
                {
                    this.position++;
                }
                bool dot = false, exponent = false;
                while (this.position < this.data.Length)
                {
                    char c = this.data[this.position];
                    if (char.IsDigit(c))
                    {
                        this.position++;
                    }
                    else if (c == '.' && !dot && !exponent)
                    {
                        dot = true;
                        this.position++;
                    }
                    else if ((c == 'e' || c == 'E') && !exponent)
                    {
                        exponent = true;
                        this.position++;
                        if (this.position < this.data.Length && (this.data[this.position] == '-' || this.data[this.position] == '+'))
                        {
                            this.position++;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                if (this.position == begin)
                    throw new FormatException("Expected a number at " + begin);
                return float.Parse(this.data.Substring(begin, this.position - begin), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
