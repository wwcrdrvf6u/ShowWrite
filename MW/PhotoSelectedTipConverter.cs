// PhotoSelectedTipConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace ShowWrite
{
    /// <summary>
    /// 照片选中状态提示转换器
    /// 用于在照片列表中显示当前选中照片的提示
    /// </summary>
    public class PhotoSelectedTipConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // values[0] 是当前列表项（PhotoWithStrokes）
                // values[1] 是当前选中的照片（CurrentPhoto）
                if (values.Length >= 2)
                {
                    var currentItem = values[0] as PhotoWithStrokes;
                    var selectedItem = values[1] as PhotoWithStrokes;

                    // 如果当前项是选中的照片，返回提示文字
                    if (currentItem != null && selectedItem != null && currentItem == selectedItem)
                    {
                        return "（当前查看）";
                    }
                }

                return string.Empty; // 不是选中的照片，返回空字符串
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}